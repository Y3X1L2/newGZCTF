using System.Diagnostics;
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
            if (!File.Exists(baseImage))
                return LibvirtAssetResult.Failed("artifact", $"VM base image is not available for digest {asset.ImageDigest}.");
            var overlay = await CreateOverlayAsync(plan, asset, baseImage, cancellationToken);
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
            return LibvirtAssetResult.Failed("compute", "VM lifecycle operation failed.");
        }
    }

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
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LibvirtAssetResult(true, "destroyed", domainName));
        }
        finally
        {
            if (domain != 0) LibvirtNativeInterop.DomainFree(domain);
        }
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
            if (new FileInfo(overlay).Length > 0 && await HasExpectedBackingAsync(overlay, baseImage, cancellationToken))
                return overlay;
            DeleteOverlay(overlay);
        }
        await RunQemuImgAsync(["create", "-f", "qcow2", "-F", "qcow2", "-b", baseImage, overlay], cancellationToken);
        return overlay;
    }

    static async Task<bool> HasExpectedBackingAsync(
        string overlay,
        string baseImage,
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
            if (!document.RootElement.TryGetProperty("backing-filename", out var backing)) return false;
            return string.Equals(Path.GetFullPath(backing.GetString() ?? string.Empty), Path.GetFullPath(baseImage),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    static string DomainName(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset) =>
        TeamLabExecutionIdentityV2.VmDomainName(plan.RuntimePublicId, plan.Generation, asset.AssetKey);

    string RuntimeDirectory(TeamLabExecutionPlanV2 plan) => Path.Combine(
        teamLab.RuntimeStateRoot, plan.RuntimePublicId.ToString("N"), plan.Generation.ToString());

    string OverlayPath(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset) => Path.Combine(
        RuntimeDirectory(plan), $"{asset.AssetKey}-{plan.PlanDigest["sha256:".Length..]}.qcow2");

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
        string domainName, string overlay)
    {
        var domain = new XElement("domain", new XAttribute("type", "kvm"),
            new XElement("name", domainName),
            new XElement("uuid", StableUuid(plan, asset)),
            new XElement("description", $"gzctf-generation={plan.Generation} gzctf-execution-plan=v2 gzctf-plan-digest={plan.PlanDigest}"),
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
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException("qemu-img failed to create the VM overlay.");
    }

    static void DeleteOverlay(string path)
    {
        if (File.Exists(path)) File.Delete(path);
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
