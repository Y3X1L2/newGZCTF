using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageSourceMigrationOptions
{
    public const string SectionName = "ImageSourceMigration";
    public string? LocalAgentConfigurationPath { get; set; }
    public string LocalAgentServiceName { get; set; } = "gzctf-agent";
}

public sealed class VmSourceBackingInspector(IOptions<KvmSettings> options,
    IOptions<ImageSourceMigrationOptions> migrationOptions) : IVmSourceBackingInspector
{
    public async Task EnsureUnusedAsync(string sourcePath, string quarantinePath, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new IOException("Source cleanup requires observable local KVM inventory.");
        var targets = new[] { Path.GetFullPath(sourcePath), Path.GetFullPath(quarantinePath) };
        bool IsTarget(string path) => targets.Any(target => VmSourceMigrationFiles.SameEntry(target, path));
        var roots = await ReadActualRootsAsync(token);
        var enumeration = new EnumerationOptions
        { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (var root in roots)
        foreach (var image in EnumerateImages(root, enumeration))
        {
            token.ThrowIfCancellationRequested();
            if (IsTarget(image)) continue;
            using var document = JsonDocument.Parse(await RunAsync("qemu-img",
                ["info", "--force-share", "--output=json", "--backing-chain", image], token));
            var nodes = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray() : [document.RootElement];
            foreach (var node in nodes)
            {
                var nodeName = node.TryGetProperty("filename", out var name) ? name.GetString() : image;
                var nodePath = Path.GetFullPath(Path.IsPathRooted(nodeName) ? nodeName! : Path.Combine(Path.GetDirectoryName(image)!, nodeName!));
                if (node.TryGetProperty("backing-filename", out var backing) && backing.GetString() is { } path &&
                    IsTarget(Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(nodePath)!, path)))
                    throw new IOException("Legacy source is still used by an actual qcow2 backing chain.");
                if (IsTarget(nodePath))
                    throw new IOException("Legacy source is still part of an actual qcow2 backing chain.");
            }
        }
        var domains = await RunAsync("virsh", ["-c", options.Value.LibvirtUri, "list", "--all", "--uuid"], token);
        foreach (var id in domains.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Guid.TryParse(id, out _)) throw new IOException("Local libvirt inventory is not observable.");
            foreach (var inactive in new[] { false, true })
            {
                var arguments = new List<string> { "-c", options.Value.LibvirtUri, "dumpxml", id };
                if (inactive) arguments.Add("--inactive");
                var domain = XDocument.Parse(await RunAsync("virsh", arguments, token));
                foreach (var file in domain.Descendants("disk").Elements("source").Attributes("file"))
                    if (IsTarget(file.Value))
                        throw new IOException("Legacy source is still attached to a local libvirt domain.");
            }
        }
    }

    async Task<string[]> ReadActualRootsAsync(CancellationToken token)
    {
        var config = migrationOptions.Value;
        if (string.IsNullOrWhiteSpace(config.LocalAgentConfigurationPath) ||
            !Path.IsPathFullyQualified(config.LocalAgentConfigurationPath) ||
            !File.Exists(config.LocalAgentConfigurationPath))
            throw new IOException("Actual local Agent configuration must be declared before source cleanup.");
        var pidText = await RunAsync("systemctl", ["show", config.LocalAgentServiceName, "--property=MainPID", "--value"], token);
        if (!int.TryParse(pidText.Trim(), out var pid) || pid <= 0)
            throw new IOException("The local Agent process is not observable.");
        var cwd = Directory.ResolveLinkTarget($"/proc/{pid}/cwd", true)?.FullName;
        if (cwd is null || !VmSourceMigrationFiles.SameEntry(Path.Combine(cwd, "appsettings.json"), config.LocalAgentConfigurationPath))
            throw new IOException("Declared configuration does not belong to the running local Agent directory.");
        if (File.GetLastWriteTimeUtc(config.LocalAgentConfigurationPath) > Process.GetProcessById(pid).StartTime.ToUniversalTime())
            throw new IOException("Agent configuration changed after startup; source cleanup cannot infer captured options.");
        // This bounded migration supports explicit base-file roots. Unknown overrides fail closed.
        var environment = (await File.ReadAllTextAsync($"/proc/{pid}/environ", token)).Split('\0');
        var command = (await File.ReadAllTextAsync($"/proc/{pid}/cmdline", token)).Split('\0');
        if (environment.Any(value => value.StartsWith("Kvm__", StringComparison.OrdinalIgnoreCase) ||
                                     value.StartsWith("TeamLab__", StringComparison.OrdinalIgnoreCase)) ||
            command.Any(value => value.Contains("ImageStoragePath", StringComparison.OrdinalIgnoreCase) ||
                                 value.Contains("RuntimeStateRoot", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Agent storage overrides require a separate observable cleanup review.");
        string imageRoot;
        string runtimeRoot;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(config.LocalAgentConfigurationPath, token));
            imageRoot = document.RootElement.GetProperty("Kvm").GetProperty("ImageStoragePath").GetString()!;
            runtimeRoot = document.RootElement.GetProperty("TeamLab").GetProperty("RuntimeStateRoot").GetString()!;
            foreach (var extra in Directory.EnumerateFiles(cwd, "appsettings.*.json"))
            {
                using var overrideDocument = JsonDocument.Parse(await File.ReadAllTextAsync(extra, token));
                if (overrideDocument.RootElement.TryGetProperty("Kvm", out _) || overrideDocument.RootElement.TryGetProperty("TeamLab", out _))
                    throw new IOException("Agent storage overrides require a separate observable cleanup review.");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new IOException("Explicit Agent image/runtime roots could not be observed."); }
        var roots = new[] { options.Value.ImageStoragePath, imageRoot, runtimeRoot };
        if (roots.Any(root => string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !Directory.Exists(root)))
            throw new IOException("Every actual image/runtime directory must be observable before source cleanup.");
        return roots.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
    }

    static IEnumerable<string> EnumerateImages(string root, EnumerationOptions options)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var directory))
        {
            var resolved = Directory.ResolveLinkTarget(directory, true)?.FullName ?? directory;
            resolved = Path.GetFullPath(resolved);
            // Do not follow an unobserved alias into arbitrary host or guest-mounted content.
            if (!resolved.StartsWith(Path.GetFullPath(root).TrimEnd('/') + "/", StringComparison.Ordinal) && resolved != Path.GetFullPath(root))
                throw new IOException("Runtime directory aliases require a separate observable cleanup review.");
            if (!visited.Add(VmSourceMigrationFiles.DirectoryIdentity(resolved))) continue;
            foreach (var file in Directory.EnumerateFiles(resolved, "*.qcow2", options)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(resolved, "*", options)) pending.Push(child);
        }
    }

    static async Task<string> RunAsync(string command, IReadOnlyList<string> arguments, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var info = new ProcessStartInfo(command)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Source reference inspector could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0) throw new IOException("Source reference inspector could not prove safe cleanup.");
            await stderr;
            return await stdout;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
