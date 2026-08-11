using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Vm;

public sealed class LibvirtTeamLabProvider(
    IOptions<KvmConfig> kvmOptions,
    IOptions<AgentTeamLabConfig> teamLabOptions,
    ILogger<LibvirtTeamLabProvider> logger)
{
    const uint UndefineManagedSave = 1;
    const uint UndefineNvram = 4;
    readonly KvmConfig kvm = kvmOptions.Value;
    readonly AgentTeamLabConfig teamLab = teamLabOptions.Value;
    readonly SemaphoreSlim lifecycleLock = new(1, 1);
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

        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var native = GetConnection();
            var domainName = expectedDomainName;
            var existing = native.Lookup(domainName);
            if (existing != 0)
            {
                try
                {
                    var state = GetState(existing);
                    if (!MatchesStableUuid(existing, plan, asset))
                        return LibvirtAssetResult.Failed("compute", "Existing VM domain has a conflicting stable identity.");
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

            var baseImage = ResolveBaseImage(asset.ImageDigest);
            if (!File.Exists(baseImage))
                return LibvirtAssetResult.Failed("artifact", $"VM base image is not available for digest {asset.ImageDigest}.");
            var overlay = CreateOverlay(plan, asset, baseImage);
            var domain = native.Define(BuildDomainXml(plan, asset, domainName, overlay));
            if (domain == 0)
            {
                DeleteOverlay(overlay);
                return LibvirtAssetResult.Failed("compute", "libvirt failed to define the VM domain.");
            }
            try
            {
                if (LibvirtNativeInterop.DomainCreate(domain) < 0)
                {
                    LibvirtNativeInterop.DomainUndefineFlags(domain, UndefineManagedSave | UndefineNvram);
                    DeleteOverlay(overlay);
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
            return LibvirtAssetResult.Failed("compute", exception.Message);
        }
        finally { lifecycleLock.Release(); }
    }

    public async Task<LibvirtAssetResult> ChangeStateAsync(
        string domainName,
        bool pause,
        CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var domain = GetConnection().Lookup(domainName);
            if (domain == 0) return LibvirtAssetResult.Failed("compute", "VM domain was not found.");
            try
            {
                var result = pause
                    ? LibvirtNativeInterop.DomainSuspend(domain)
                    : LibvirtNativeInterop.DomainResume(domain);
                return result < 0
                    ? LibvirtAssetResult.Failed("compute", "libvirt lifecycle operation failed.")
                    : new LibvirtAssetResult(true, pause ? "paused" : "running", domainName);
            }
            finally { LibvirtNativeInterop.DomainFree(domain); }
        }
        finally { lifecycleLock.Release(); }
    }

    public async Task<LibvirtAssetResult> DestroyAsync(
        string domainName,
        CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var domain = GetConnection().Lookup(domainName);
            if (domain == 0) return new LibvirtAssetResult(true, "destroyed", domainName);
            try
            {
                LibvirtNativeInterop.DomainDestroy(domain);
                if (LibvirtNativeInterop.DomainUndefineFlags(domain, UndefineManagedSave | UndefineNvram) < 0)
                    return LibvirtAssetResult.Failed("cleanup", "libvirt failed to undefine the VM domain.");
                return new LibvirtAssetResult(true, "destroyed", domainName);
            }
            finally { LibvirtNativeInterop.DomainFree(domain); }
        }
        finally { lifecycleLock.Release(); }
    }

    LibvirtConnection GetConnection() => connection ??= LibvirtConnection.TryOpen(logger, kvm.LibvirtUri)
        ?? throw new InvalidOperationException("Native libvirt is unavailable.");

    public IReadOnlyList<TeamLabExecutionInventoryFactV2> GetInventory(
        TeamLabExecutionPlanV2 plan)
    {
        var result = new List<TeamLabExecutionInventoryFactV2>();
        var native = GetConnection();
        foreach (var asset in plan.Assets.Where(item =>
                     string.Equals(item.Kind, "vm", StringComparison.OrdinalIgnoreCase)))
        {
            var name = DomainName(plan, asset);
            var domain = native.Lookup(name);
            if (domain == 0) continue;
            try
            {
                result.Add(new TeamLabExecutionInventoryFactV2(
                    "vm", asset.AssetKey, name, GetState(domain), plan.Generation));
            }
            finally { LibvirtNativeInterop.DomainFree(domain); }
        }
        return result;
    }

    public LibvirtAssetResult Destroy(
        TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset)
    {
        var domainName = DomainName(plan, asset);
        var domain = GetConnection().Lookup(domainName);
        try
        {
            if (domain != 0)
            {
                if (GetState(domain) is "running" or "paused" &&
                    LibvirtNativeInterop.DomainDestroy(domain) < 0)
                    return LibvirtAssetResult.Failed("cleanup", "libvirt failed to destroy the VM domain.");
                if (LibvirtNativeInterop.DomainUndefineFlags(domain, UndefineManagedSave | UndefineNvram) < 0)
                    return LibvirtAssetResult.Failed("cleanup", "libvirt failed to undefine the VM domain.");
            }
            var overlay = Path.Combine(teamLab.RuntimeStateRoot,
                plan.RuntimePublicId.ToString("N"), plan.Generation.ToString(), $"{asset.AssetKey}.qcow2");
            DeleteOverlay(overlay);
            return new LibvirtAssetResult(true, "destroyed", domainName);
        }
        finally
        {
            if (domain != 0) LibvirtNativeInterop.DomainFree(domain);
        }
    }

    string ResolveBaseImage(string digest)
    {
        var safe = new string((digest ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length < 16) throw new InvalidOperationException("Artifact digest is invalid.");
        return Path.Combine(kvm.ImageStoragePath, $"{safe}.qcow2");
    }

    string CreateOverlay(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset, string baseImage)
    {
        var root = Path.Combine(teamLab.RuntimeStateRoot, plan.RuntimePublicId.ToString("N"), plan.Generation.ToString());
        Directory.CreateDirectory(root);
        var overlay = Path.Combine(root, $"{asset.AssetKey}.qcow2");
        if (File.Exists(overlay)) return overlay;
        RunQemuImg(["create", "-f", "qcow2", "-F", "qcow2", "-b", baseImage, overlay]);
        return overlay;
    }

    static string DomainName(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset) =>
        $"gzctf-tl-{plan.RuntimePublicId:N}-{plan.Generation}-{SanitizeAssetKey(asset.AssetKey)}";

    static string SanitizeAssetKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
                builder.Append(character);
        return builder.Length == 0 ? "asset" : builder.ToString()[..Math.Min(48, builder.Length)];
    }

    static bool MatchesStableUuid(nint domain, TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset)
    {
        var buffer = new StringBuilder(37);
        return LibvirtNativeInterop.DomainGetUuidString(domain, buffer) == 0 &&
               string.Equals(buffer.ToString(), StableUuid(plan, asset), StringComparison.OrdinalIgnoreCase);
    }

    string BuildDomainXml(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset,
        string domainName, string overlay)
    {
        var domain = new XElement("domain", new XAttribute("type", "kvm"),
            new XElement("name", domainName),
            new XElement("uuid", StableUuid(plan, asset)),
            new XElement("memory", new XAttribute("unit", "MiB"), Math.Max(256, asset.MemoryMiB)),
            new XElement("currentMemory", new XAttribute("unit", "MiB"), Math.Max(256, asset.MemoryMiB)),
            new XElement("vcpu", Math.Clamp(asset.Cpu, 1, 64)),
            new XElement("os", new XElement("type", new XAttribute("arch", "x86_64"), "hvm")),
            new XElement("devices",
                new XElement("disk", new XAttribute("type", "file"), new XAttribute("device", "disk"),
                    new XElement("driver", new XAttribute("name", "qemu"), new XAttribute("type", "qcow2")),
                    new XElement("source", new XAttribute("file", overlay)),
                    new XElement("target", new XAttribute("dev", "vda"), new XAttribute("bus", "virtio"))),
                asset.NetworkAttachments.Select(attachment => NetworkInterface(plan, asset, attachment))));
        return domain.ToString(SaveOptions.DisableFormatting);
    }

    XElement NetworkInterface(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset,
        TeamLabAssetNetworkAttachmentV2 attachment)
    {
        var port = plan.Networks.SelectMany(item => item.Ports)
            .FirstOrDefault(item => item.Key == attachment.PortKey);
        return new XElement("interface", new XAttribute("type", "bridge"),
            new XElement("mac", new XAttribute("address", port?.MacAddress ?? "52:54:00:00:00:01")),
            new XElement("source", new XAttribute("bridge", teamLab.OvsIntegrationBridgeName)),
            new XElement("virtualport", new XAttribute("type", "openvswitch"),
                new XElement("parameters", new XAttribute("interfaceid",
                    TeamLabOvnNaming.LogicalPortName(plan, attachment.NetworkKey, attachment.PortKey)))),
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

    static void RunQemuImg(IReadOnlyList<string> arguments)
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
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"qemu-img failed: {process.StandardError.ReadToEnd()}");
    }

    static void DeleteOverlay(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed record LibvirtAssetResult(bool Success, string State, string ResourceId)
{
    public static LibvirtAssetResult Failed(string _, string message) => new(false, message, string.Empty);
}
