using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Vm;

public sealed class LibvirtTeamLabProvider(
    IOptions<KvmConfig> kvmOptions,
    IOptions<AgentTeamLabConfig> teamLabOptions,
    AgentResourceLock resourceLock,
    ILogger<LibvirtTeamLabProvider> logger) : IDisposable
{
    const uint UndefineManagedSave = 1;
    const uint UndefineNvram = 4;
    readonly KvmConfig kvm = kvmOptions.Value;
    readonly AgentTeamLabConfig teamLab = teamLabOptions.Value;
    readonly object connectionSync = new();
    LibvirtConnection? connection;

    public async Task<LibvirtAssetResult> EnsureRunningAsync(
        TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(asset.Kind, "vm", StringComparison.OrdinalIgnoreCase))
            return LibvirtAssetResult.Failed("validation", "The libvirt provider accepts VM assets only.");
        if (plan.Generation <= 0 || string.IsNullOrWhiteSpace(asset.ResourceId))
            return LibvirtAssetResult.Failed("validation", "VM identity is invalid.");
        var expectedDomainName = DomainName(plan, asset);
        if (!string.Equals(asset.ResourceId, expectedDomainName, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(asset.DomainIdentity) &&
             !string.Equals(asset.DomainIdentity, expectedDomainName, StringComparison.Ordinal)))
            return LibvirtAssetResult.Failed("validation", "VM identity does not match the execution plan generation.");

        try
        {
            var native = GetConnection();
            var domainName = expectedDomainName;
            var existing = native.Lookup(domainName);
            if (existing != 0)
            {
                try
                {
                    // Validate ownership before any destructive libvirt operation. A name is
                    // deterministic, but it is not an ownership proof when a stale or foreign
                    // domain occupies it.
                    if (!MatchesStableUuid(existing, plan, asset))
                        return LibvirtAssetResult.Failed("compute", "Existing VM domain has a conflicting stable identity.");
                    if (!MatchesExecutionPlan(native.GetXml(existing), plan))
                        return LibvirtAssetResult.Failed("compute", "Existing VM domain belongs to a different execution plan.");
                    var state = GetState(existing);
                    if (state == "running")
                        return new LibvirtAssetResult(true, state, domainName);
                    var startResult = state == "paused"
                        ? LibvirtNativeInterop.DomainResume(existing)
                        : state == "shutoff"
                            ? LibvirtNativeInterop.DomainCreate(existing)
                            : -1;
                    return startResult < 0
                        ? LibvirtAssetResult.Failed("compute", $"VM domain could not be resumed from state: {state}.")
                        : new LibvirtAssetResult(true, "running", domainName);
                }
                finally { LibvirtNativeInterop.DomainFree(existing); }
            }

            var baseImage = ResolveBaseImage(plan, asset);
            await using var imageLock = await resourceLock.AcquireAsync(
                BaseImageLockKey(baseImage), cancellationToken);
            if (!File.Exists(baseImage))
                return LibvirtAssetResult.Failed("artifact", $"VM base image is not available for digest {asset.ImageDigest}.");
            if (!await HasExpectedBaseImageAsync(baseImage, asset.ImageDigest, cancellationToken))
                return LibvirtAssetResult.Failed("artifact", "VM base image does not match the execution-plan digest.");
            var overlay = await CreateOverlayAsync(plan, asset, baseImage, cancellationToken);
            var networkSeed = await CreateNetworkSeedAsync(plan, asset, cancellationToken);
            var domain = native.Define(BuildDomainXml(plan, asset, domainName, overlay, networkSeed));
            if (domain == 0)
            {
                DeleteOverlay(overlay);
                DeleteNetworkSeed(plan, asset);
                return LibvirtAssetResult.Failed("compute", "libvirt failed to define the VM domain.");
            }
            try
            {
                if (LibvirtNativeInterop.DomainCreate(domain) < 0)
                {
                    LibvirtNativeInterop.DomainUndefineFlags(domain, UndefineManagedSave | UndefineNvram);
                    DeleteOverlay(overlay);
                    DeleteNetworkSeed(plan, asset);
                    return LibvirtAssetResult.Failed("compute", "libvirt failed to start the VM domain.");
                }
                return new LibvirtAssetResult(true, "running", domainName);
            }
            finally { LibvirtNativeInterop.DomainFree(domain); }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or IOException)
        {
            logger.LogWarning(exception, "TeamLab native libvirt failed for runtime {RuntimeId}, asset {AssetKey}",
                plan.RuntimeId, asset.AssetKey);
            return LibvirtAssetResult.Failed("compute", "VM lifecycle operation failed.");
        }
    }

    public Task<LibvirtAssetResult> ChangePowerAsync(
        TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset, string action,
        string? expectedNativeIdentity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var native = GetConnection();
        var domain = native.Lookup(DomainName(plan, asset));
        try
        {
            if (domain == 0 || !MatchesStableUuid(domain, plan, asset) || !MatchesExecutionPlan(native.GetXml(domain), plan))
                return Task.FromResult(LibvirtAssetResult.Failed("compute", "VM execution identity changed."));
            var identity = GetInventory(plan).SingleOrDefault(item => item.AssetKey == asset.AssetKey)?.NativeIdentity;
            if (expectedNativeIdentity is not null && identity != expectedNativeIdentity)
                return Task.FromResult(LibvirtAssetResult.Failed("compute", "VM native identity changed."));
            var state = GetState(domain);
            var result = action switch
            {
                "inspect" => 0,
                "stop" when state == "shutoff" => 0,
                "stop" => LibvirtNativeInterop.DomainDestroy(domain),
                "pause" when state == "paused" => 0,
                "pause" when state == "running" => LibvirtNativeInterop.DomainSuspend(domain),
                "resume" when state == "running" => 0,
                "resume" when state == "paused" => LibvirtNativeInterop.DomainResume(domain),
                _ => -1
            };
            return Task.FromResult(result < 0 ? LibvirtAssetResult.Failed("compute", "VM power operation failed.")
                : new LibvirtAssetResult(true, GetState(domain), asset.ResourceId));
        }
        finally { if (domain != 0) LibvirtNativeInterop.DomainFree(domain); }
    }

    public Task<LibvirtAssetResult> PauseAsync(
        string domainName,
        int expectedGeneration,
        CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(domainName, expectedGeneration, pause: true, cancellationToken);

    public Task<LibvirtAssetResult> ResumeAsync(
        string domainName,
        int expectedGeneration,
        CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(domainName, expectedGeneration, pause: false, cancellationToken);

    Task<LibvirtAssetResult> ChangeLifecycleAsync(
        string domainName,
        int expectedGeneration,
        bool pause,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(domainName) || expectedGeneration <= 0)
            return Task.FromResult(LibvirtAssetResult.Failed("validation", "VM lifecycle identity is invalid."));
        var domain = GetConnection().Lookup(domainName);
        try
        {
            if (domain == 0)
                return Task.FromResult(LibvirtAssetResult.Failed("compute", "VM domain is not present on this node."));
            if (!MatchesLifecycleOwnership(GetConnection().GetXml(domain), expectedGeneration))
                return Task.FromResult(LibvirtAssetResult.Failed(
                    "compute", "VM domain does not belong to the requested runtime generation."));
            var state = GetState(domain);
            if (pause ? state == "paused" : state == "running")
                return Task.FromResult(new LibvirtAssetResult(true, state, domainName));
            var result = pause
                ? LibvirtNativeInterop.DomainSuspend(domain)
                : LibvirtNativeInterop.DomainResume(domain);
            return Task.FromResult(result < 0
                ? LibvirtAssetResult.Failed("compute",
                    $"libvirt failed to {(pause ? "suspend" : "resume")} the VM domain.")
                : new LibvirtAssetResult(true, pause ? "paused" : "running", domainName));
        }
        finally
        {
            if (domain != 0) LibvirtNativeInterop.DomainFree(domain);
        }
    }

    static bool MatchesLifecycleOwnership(string? xml, int expectedGeneration) =>
        !string.IsNullOrWhiteSpace(xml) &&
        xml.Contains($"gzctf-generation={expectedGeneration}", StringComparison.Ordinal) &&
        xml.Contains("gzctf-execution-plan=v2", StringComparison.Ordinal);

    LibvirtConnection GetConnection()
    {
        lock (connectionSync)
            return connection ??= LibvirtConnection.TryOpen(logger, kvm.LibvirtUri)
                ?? throw new InvalidOperationException("Native libvirt is unavailable.");
    }

    public IReadOnlyList<TeamLabExecutionInventoryFactV2> GetInventory(
        TeamLabExecutionPlanV2 plan)
    {
        var result = new List<TeamLabExecutionInventoryFactV2>();
        var native = GetConnection();
        foreach (var domain in native.ListDomains())
        {
            try
            {
                var name = native.GetName(domain);
                if (string.IsNullOrWhiteSpace(name) || !MatchesExecutionPlan(native.GetXml(domain), plan))
                    continue;
                var asset = plan.Assets.FirstOrDefault(item =>
                    item.Kind.Equals("vm", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(DomainName(plan, item), name, StringComparison.Ordinal));
                var uuid = new StringBuilder(37);
                if (LibvirtNativeInterop.DomainGetUuidString(domain, uuid) < 0 || !Guid.TryParse(uuid.ToString(), out _))
                    throw new InvalidOperationException("libvirt failed to read the VM native identity.");
                result.Add(new TeamLabExecutionInventoryFactV2(
                    "vm", asset?.AssetKey ?? $"residual:{name}", name, GetState(domain), plan.Generation, uuid.ToString()));
            }
            finally { LibvirtNativeInterop.DomainFree(domain); }
        }
        return result;
    }

    public Task<LibvirtAssetResult> DestroyAsync(
        TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var domainName = DomainName(plan, asset);
        var domain = GetConnection().Lookup(domainName);
        try
        {
            if (domain != 0)
            {
                if (!MatchesStableUuid(domain, plan, asset) ||
                    !MatchesExecutionPlan(GetConnection().GetXml(domain), plan))
                    return Task.FromResult(LibvirtAssetResult.Failed("cleanup",
                        "VM domain does not belong to the requested execution plan."));
                if (GetState(domain) is "running" or "paused" &&
                    LibvirtNativeInterop.DomainDestroy(domain) < 0)
                    return Task.FromResult(LibvirtAssetResult.Failed("cleanup", "libvirt failed to destroy the VM domain."));
                if (LibvirtNativeInterop.DomainUndefineFlags(domain, UndefineManagedSave | UndefineNvram) < 0)
                    return Task.FromResult(LibvirtAssetResult.Failed("cleanup", "libvirt failed to undefine the VM domain."));
            }
            var overlay = OverlayPath(plan, asset);
            DeleteOverlay(overlay);
            DeleteNetworkSeed(plan, asset);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LibvirtAssetResult(true, "destroyed", domainName));
        }
        finally
        {
            if (domain != 0) LibvirtNativeInterop.DomainFree(domain);
        }
    }

    public Task<LibvirtAssetResult> DestroyResidualsAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var native = GetConnection();
        var declared = plan.Assets
            .Where(item => item.Kind.Equals("vm", StringComparison.OrdinalIgnoreCase))
            .Select(item => DomainName(plan, item))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var domain in native.ListDomains())
        {
            try
            {
                var name = native.GetName(domain);
                var xml = native.GetXml(domain);
                if (string.IsNullOrWhiteSpace(name) || declared.Contains(name) || !MatchesExecutionPlan(xml, plan))
                    continue;
                if (GetState(domain) is "running" or "paused" &&
                    LibvirtNativeInterop.DomainDestroy(domain) < 0)
                    return Task.FromResult(LibvirtAssetResult.Failed("cleanup",
                        $"libvirt failed to destroy residual VM {name}."));
                if (LibvirtNativeInterop.DomainUndefineFlags(domain, UndefineManagedSave | UndefineNvram) < 0)
                    return Task.FromResult(LibvirtAssetResult.Failed("cleanup",
                        $"libvirt failed to undefine residual VM {name}."));
                DeleteResidualOverlay(xml, plan);
            }
            finally { LibvirtNativeInterop.DomainFree(domain); }
        }
        return Task.FromResult(new LibvirtAssetResult(true, "destroyed", string.Empty));
    }

    string ResolveBaseImage(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset)
    {
        if (asset.TemplateId <= 0)
            throw new InvalidOperationException("VM artifact identity is missing from the execution plan.");
        return Path.Combine(kvm.ImageStoragePath, $"{asset.TemplateId}.qcow2");
    }

    async Task<string> CreateOverlayAsync(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset,
        string baseImage, CancellationToken cancellationToken)
    {
        var root = RuntimeDirectory(plan);
        Directory.CreateDirectory(root);
        var overlay = OverlayPath(plan, asset);
        if (File.Exists(overlay))
        {
            if (new FileInfo(overlay).Length > 0 && await HasExpectedBackingAsync(
                    overlay, baseImage, asset.ImageDigest, cancellationToken))
                return overlay;
            DeleteOverlay(overlay);
        }
        await RunQemuImgAsync(["create", "-f", "qcow2", "-F", "qcow2", "-b", baseImage, overlay], cancellationToken);
        return overlay;
    }

    async Task<bool> HasExpectedBackingAsync(
        string overlay,
        string baseImage,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "qemu-img",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("info");
        process.StartInfo.ArgumentList.Add("--output=json");
        process.StartInfo.ArgumentList.Add(overlay);
        process.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0) return false;
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("backing-filename", out var backing) ||
                !document.RootElement.TryGetProperty("format", out var format) ||
                !string.Equals(format.GetString(), "qcow2", StringComparison.OrdinalIgnoreCase) ||
                !document.RootElement.TryGetProperty("virtual-size", out var virtualSize) ||
                virtualSize.GetInt64() <= 0)
                return false;
            return string.Equals(Path.GetFullPath(backing.GetString() ?? string.Empty), Path.GetFullPath(baseImage),
                       OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                   await HasExpectedBaseImageAsync(baseImage, expectedDigest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    internal static async Task<bool> HasExpectedBaseImageAsync(
        string path, string? expectedDigest, CancellationToken cancellationToken)
    {
        var digest = NormalizeSha256(expectedDigest);
        if (digest is null) return false;
        if (!File.Exists(path)) return false;
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return string.Equals(actual, digest, StringComparison.Ordinal);
    }

    internal static string BaseImageLockKey(string path) =>
        "vm-image-path:" + Path.GetFullPath(path).Replace('\\', '/').ToUpperInvariant();

    static string? NormalizeSha256(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[7..];
        return trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit) ? trimmed.ToLowerInvariant() : null;
    }

    static string DomainName(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset) =>
        TeamLabExecutionIdentityV2.VmDomainName(plan.RuntimePublicId, plan.Generation, plan.ShardKey, asset.AssetKey);

    string RuntimeDirectory(TeamLabExecutionPlanV2 plan) => Path.Combine(
        teamLab.RuntimeStateRoot, plan.RuntimePublicId.ToString("N"), plan.Generation.ToString());

    string OverlayPath(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset) => Path.Combine(
        RuntimeDirectory(plan), $"{asset.AssetKey}-{PlanDigestHex(plan.PlanDigest)}.qcow2");

    string NetworkSeedDirectory(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset) => Path.Combine(
        RuntimeDirectory(plan), $"{asset.AssetKey}-{PlanDigestHex(plan.PlanDigest)}-seed");

    static string PlanDigestHex(string digest) =>
        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : digest;

    static bool MatchesStableUuid(nint domain, TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset)
    {
        var buffer = new StringBuilder(37);
        return LibvirtNativeInterop.DomainGetUuidString(domain, buffer) == 0 &&
               string.Equals(buffer.ToString(), StableUuid(plan, asset), StringComparison.OrdinalIgnoreCase);
    }

    static bool MatchesExecutionPlan(string? xml, TeamLabExecutionPlanV2 plan) =>
        !string.IsNullOrWhiteSpace(xml) && xml.Contains(
            $"gzctf-generation={plan.Generation} gzctf-execution-plan=v2 gzctf-plan-digest={plan.PlanDigest}",
            StringComparison.Ordinal);

    string BuildDomainXml(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset,
        string domainName, string overlay, string? networkSeed)
    {
        var domain = new XElement("domain", new XAttribute("type", "kvm"),
            new XElement("name", domainName),
            new XElement("uuid", StableUuid(plan, asset)),
            new XElement("description", $"gzctf-generation={plan.Generation} gzctf-execution-plan=v2 gzctf-plan-digest={plan.PlanDigest}"),
            new XElement("memory", new XAttribute("unit", "MiB"), Math.Max(256, asset.MemoryMiB)),
            new XElement("currentMemory", new XAttribute("unit", "MiB"), Math.Max(256, asset.MemoryMiB)),
            new XElement("vcpu", Math.Clamp(asset.Cpu, 1, 64)),
            new XElement("os", new XElement("type", new XAttribute("arch", "x86_64"), "hvm")),
            asset.Device is { } device ? new XElement("sysinfo", new XAttribute("type", "fwcfg"),
                new XElement("entry", new XAttribute("name", "opt/org.gzctf/device-parameters"), device.ParametersJson)) : null,
            new XElement("devices",
                new XElement("graphics", new XAttribute("type", "vnc"), new XAttribute("autoport", "yes"),
                    new XAttribute("listen", "127.0.0.1"),
                    new XElement("listen", new XAttribute("type", "address"), new XAttribute("address", "127.0.0.1"))),
                new XElement("video", new XElement("model", new XAttribute("type", "vga"))),
                new XElement("disk", new XAttribute("type", "file"), new XAttribute("device", "disk"),
                    new XElement("driver", new XAttribute("name", "qemu"), new XAttribute("type", "qcow2")),
                    new XElement("source", new XAttribute("file", overlay)),
                    new XElement("target", new XAttribute("dev", "vda"), new XAttribute("bus", "virtio"))),
                networkSeed is null ? null : new XElement("disk", new XAttribute("type", "file"),
                    new XAttribute("device", "cdrom"),
                    new XElement("driver", new XAttribute("name", "qemu"), new XAttribute("type", "raw")),
                    new XElement("source", new XAttribute("file", networkSeed)),
                    new XElement("target", new XAttribute("dev", "sda"), new XAttribute("bus", "sata")),
                    new XElement("readonly")),
                new XElement("channel", new XAttribute("type", "unix"),
                    new XElement("target", new XAttribute("type", "virtio"),
                        new XAttribute("name", "org.qemu.guest_agent.0"))),
                asset.NetworkAttachments.Select(attachment => NetworkInterface(plan, asset, attachment))));
        return domain.ToString(SaveOptions.DisableFormatting);
    }

    async Task<string?> CreateNetworkSeedAsync(
        TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset,
        CancellationToken token)
    {
        var config = BuildNoCloudNetworkConfig(plan, asset);
        if (config is null) return null;

        var root = NetworkSeedDirectory(plan, asset);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        var metaData = Path.Combine(root, "meta-data");
        var userData = Path.Combine(root, "user-data");
        var networkConfig = Path.Combine(root, "network-config");
        var iso = Path.Combine(root, "seed.iso");
        await File.WriteAllTextAsync(metaData,
            $"instance-id: {StableUuid(plan, asset)}\nlocal-hostname: {asset.AssetKey}\n", token);
        await File.WriteAllTextAsync(userData, "#cloud-config\nmanage_etc_hosts: true\n", token);
        await File.WriteAllTextAsync(networkConfig, config, token);

        var tool = ResolveExecutable("cloud-localds", "genisoimage", "mkisofs", "xorriso")
                   ?? throw new InvalidOperationException(
                       "VM network seed requires cloud-localds, genisoimage, mkisofs, or xorriso.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = tool,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root
            }
        };
        var name = Path.GetFileName(tool);
        var arguments = name == "cloud-localds"
            ? new[] { $"--network-config={networkConfig}", iso, userData, metaData }
            : name == "xorriso"
                ? new[]
                {
                    "-as", "mkisofs", "-quiet", "-output", iso, "-volid", "CIDATA", "-joliet", "-rock",
                    "user-data", "meta-data", "network-config"
                }
                : new[]
                {
                    "-quiet", "-output", iso, "-volid", "CIDATA", "-joliet", "-rock",
                    "user-data", "meta-data", "network-config"
                };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var error = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0 || !File.Exists(iso) || new FileInfo(iso).Length == 0)
            throw new InvalidOperationException($"Failed to create VM network seed: {error.Trim()}");
        return iso;
    }

    internal static string? ResolveExecutable(params string[] names)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var name in names)
            foreach (var path in paths)
            {
                var candidate = Path.Combine(path, name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    internal static string? BuildNoCloudNetworkConfig(
        TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset)
    {
        var configured = asset.NetworkAttachments
            .Select((attachment, index) =>
            {
                var network = plan.Networks.SingleOrDefault(item => item.Key == attachment.NetworkKey);
                var port = network?.Ports.SingleOrDefault(item => item.Key == attachment.PortKey);
                var prefix = network?.Cidr.Split('/').LastOrDefault();
                return new { Attachment = attachment, Index = index, Network = network, Port = port, Prefix = prefix };
            })
            .Where(item => item.Network is not null && item.Port is not null &&
                           IPAddress.TryParse(item.Attachment.IpAddress, out _) &&
                           int.TryParse(item.Prefix, out var prefix) && prefix is >= 1 and <= 32)
            .ToArray();
        if (configured.Length == 0) return null;

        var builder = new StringBuilder("version: 2\nethernets:\n");
        foreach (var item in configured)
        {
            builder.AppendLine($"  nic{item.Index}:");
            builder.AppendLine("    match:");
            builder.AppendLine($"      macaddress: \"{item.Port!.MacAddress.ToLowerInvariant()}\"");
            builder.AppendLine($"    set-name: \"{item.Attachment.InterfaceName}\"");
            builder.AppendLine("    addresses:");
            builder.AppendLine($"      - {item.Attachment.IpAddress}/{item.Prefix}");
            if (item.Attachment.Primary && IPAddress.TryParse(item.Attachment.GatewayIp, out _))
            {
                builder.AppendLine("    routes:");
                builder.AppendLine("      - to: default");
                builder.AppendLine($"        via: {item.Attachment.GatewayIp}");
            }
        }
        return builder.ToString();
    }

    void DeleteNetworkSeed(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset)
    {
        var path = NetworkSeedDirectory(plan, asset);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    XElement NetworkInterface(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset,
        TeamLabAssetNetworkAttachmentV2 attachment)
    {
        var port = plan.Networks
            .FirstOrDefault(network => network.Key == attachment.NetworkKey)?
            .Ports.FirstOrDefault(item => item.Key == attachment.PortKey);
        if (port is null || string.IsNullOrWhiteSpace(port.MacAddress))
            throw new InvalidOperationException("VM network attachment is missing its declared port identity.");
        return new XElement("interface", new XAttribute("type", "bridge"),
            new XElement("mac", new XAttribute("address", port.MacAddress)),
            new XElement("source", new XAttribute("bridge", teamLab.OvsIntegrationBridgeName)),
            new XElement("virtualport", new XAttribute("type", "openvswitch"),
                new XElement("parameters", new XAttribute("interfaceid",
                    TeamLabOvnNaming.LogicalPortId(plan, attachment.NetworkKey, attachment.PortKey)))),
            new XElement("target", new XAttribute("dev",
                TeamLabExecutionIdentityV2.VmTapName(plan.RuntimePublicId, plan.Generation, asset.AssetKey, attachment.NetworkKey))),
            new XElement("model", new XAttribute("type", "virtio")),
            new XElement("alias", new XAttribute("name", attachment.InterfaceName)));
    }

    static string StableUuid(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes($"gzctf:{plan.RuntimePublicId:D}:{plan.Generation}:{asset.AssetKey}"));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes[..16]).ToString();
    }

    static string GetState(nint domain)
    {
        if (LibvirtNativeInterop.DomainGetState(domain, out var state, out _, 0) < 0)
            return "unknown";
        return state switch { 1 => "running", 3 => "paused", 5 => "shutoff", _ => "unknown" };
    }

    static async Task RunQemuImgAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "qemu-img",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var standardError = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            _ = await standardError;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("qemu-img failed to create the VM overlay.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    static void DeleteOverlay(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    void DeleteResidualOverlay(string? xml, TeamLabExecutionPlanV2 plan)
    {
        if (string.IsNullOrWhiteSpace(xml)) return;
        try
        {
            var root = Path.GetFullPath(teamLab.RuntimeStateRoot).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var overlay = XDocument.Parse(xml).Root?
                .Descendants("source")
                .FirstOrDefault(source => source.Parent?.Name.LocalName == "disk")
                ?.Attribute("file")?.Value;
            if (string.IsNullOrWhiteSpace(overlay)) return;
            var path = Path.GetFullPath(overlay);
            if (path.StartsWith(root, StringComparison.Ordinal) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to inspect the residual VM overlay for runtime {RuntimeId}.", plan.RuntimeId);
        }
    }

    public void Dispose()
    {
        lock (connectionSync)
        {
            connection?.Dispose();
            connection = null;
        }
    }
}

public sealed record LibvirtAssetResult(bool Success, string State, string ResourceId)
{
    public static LibvirtAssetResult Failed(string _, string message) => new(false, message, string.Empty);
}
