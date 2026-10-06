using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Modules.TeamLab.Application;

internal static class TeamLabRuntimeAssetFacts
{
    internal sealed record ExecutedAsset(TeamLabAssetExecutionSpecV2 Spec, bool OperatingSystemRecorded);
    private sealed record InterfaceAllocation(
        string Key, string NetworkKey, string IpAddress, int PrefixLength,
        string MacAddress, bool Primary);

    internal static bool IsRunning(TeamLabRuntimeAsset asset, TeamLabRuntimeStatus runtimeStatus,
        int runtimeGeneration) =>
        asset.Generation == runtimeGeneration &&
        runtimeStatus == TeamLabRuntimeStatus.Running &&
        (asset.Status is TeamLabRuntimeStatus.Pending or TeamLabRuntimeStatus.Planning or
            TeamLabRuntimeStatus.Scheduled or TeamLabRuntimeStatus.Deploying or
            TeamLabRuntimeStatus.Probing or TeamLabRuntimeStatus.Running) &&
        asset.WorkerNodeId is not null && !string.IsNullOrWhiteSpace(asset.RuntimeResourceId);

    internal static IReadOnlyDictionary<string, ExecutedAsset> ReadCurrentSpecs(
        TeamLabRuntime runtime,
        IReadOnlyList<TeamLabExecutionPlanSnapshot> snapshots)
    {
        var specs = new Dictionary<string, ExecutedAsset>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots.Where(item => item.RuntimeId == runtime.Id &&
                     item.Generation == runtime.Generation))
        {
            try
            {
                var json = snapshot.CurrentPlanJson ?? snapshot.PlanJson;
                var plan = JsonSerializer.Deserialize<TeamLabExecutionPlanV2>(json);
                if (plan?.RuntimeId != runtime.Id || plan.RuntimePublicId != runtime.PublicId ||
                    plan.Generation != runtime.Generation || plan.Assets is null) continue;
                using var document = JsonDocument.Parse(json);
                var recordedOs = new HashSet<string>(StringComparer.Ordinal);
                if (document.RootElement.TryGetProperty("Assets", out var assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                    foreach (var item in assets.EnumerateArray())
                        if (item.TryGetProperty("AssetKey", out var key) &&
                            key.ValueKind == JsonValueKind.String &&
                            item.TryGetProperty("OperatingSystem", out _))
                            recordedOs.Add(key.GetString()!);
                foreach (var spec in plan.Assets)
                    specs.TryAdd(spec.AssetKey, new(spec, recordedOs.Contains(spec.AssetKey)));
            }
            catch (JsonException)
            {
                // Legacy or damaged snapshot: the runtime remains readable without invented facts.
            }
        }
        return specs;
    }

    internal static (string Value, string Source) OperatingSystem(
        TeamLabRuntimeAsset asset, ExecutedAsset? executed, ImageRuntimeAccessSummary? template)
    {
        var spec = executed?.Spec;
        if (asset.Kind != TeamLabResourceKind.Vm || spec is null ||
            spec.Kind != "vm" || spec.TemplateId != asset.SourceTemplateId ||
            !SameImageDigest(spec.ImageDigest, asset.ImageDigest))
            return ("unknown", "unknown");
        if (!executed!.OperatingSystemRecorded)
            return template is not null && template.TemplateId == asset.SourceTemplateId &&
                   SameImageDigest(template.ImageHash, asset.ImageDigest)
                ? (template.OperatingSystem == OSType.Windows ? "windows" : "linux", "template-current")
                : ("unknown", "unknown");
        var value = spec.OperatingSystem switch
        {
            TeamLabGuestOperatingSystem.Windows => "windows",
            TeamLabGuestOperatingSystem.Linux => "linux",
            _ => "unknown"
        };
        return (value, value == "unknown" ? "unknown" : "execution-plan");
    }

    internal static IReadOnlyList<TeamLabRuntimeInterfaceProjectionModel> Interfaces(
        TeamLabRuntimeAsset asset, ExecutedAsset? executed)
    {
        var spec = executed?.Spec;
        var expectedKind = asset.Kind == TeamLabResourceKind.Docker ? "docker" : "vm";
        if (spec is null || !string.Equals(spec.Kind, expectedKind, StringComparison.OrdinalIgnoreCase) ||
            spec.TemplateId != asset.SourceTemplateId ||
            !SameImageDigest(spec.ImageDigest, asset.ImageDigest)) return [];
        InterfaceAllocation[] allocations;
        try
        {
            allocations = JsonSerializer.Deserialize<InterfaceAllocation[]>(asset.InterfaceSummaryJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        if (allocations.Length == 0) return [];
        return allocations.Select(allocation =>
        {
            var attachment = spec.NetworkAttachments.FirstOrDefault(item =>
                (string.Equals(item.InterfaceKey, allocation.Key, StringComparison.Ordinal) ||
                 item.InterfaceKey is null && string.Equals(item.NetworkKey, allocation.NetworkKey, StringComparison.Ordinal)) &&
                string.Equals(item.NetworkKey, allocation.NetworkKey, StringComparison.Ordinal) &&
                string.Equals(item.IpAddress, allocation.IpAddress, StringComparison.Ordinal) &&
                (item.PrefixLength is null || item.PrefixLength == allocation.PrefixLength));
            TeamLabRuntimeInterfaceValuesModel? assigned = attachment is null ? null : new(
                allocation.IpAddress, allocation.PrefixLength,
                attachment.DnsServers ?? (attachment.DnsServerIp is { } dns ? [dns] : []),
                attachment.GatewayIp,
                attachment.StaticRoutes?.Select(route => new TeamLabGuestRouteModel(
                    route.DestinationCidr, route.NextHop, route.Metric)).ToArray() ?? []);
            return new TeamLabRuntimeInterfaceProjectionModel(
                allocation.Key, allocation.NetworkKey, allocation.Primary, assigned, null);
        }).ToArray();
    }

    private static bool SameImageDigest(string? left, string? right)
    {
        var canonicalLeft = CanonicalSha256(left);
        return canonicalLeft is not null &&
               string.Equals(canonicalLeft, CanonicalSha256(right), StringComparison.Ordinal);
    }

    // Runtime assets retain the source hash; accepted execution plans use a prefixed digest.
    private static string? CanonicalSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digest = value.Trim();
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            digest = digest["sha256:".Length..];
        return digest.Length == 64 && digest.All(Uri.IsHexDigit)
            ? $"sha256:{digest.ToLowerInvariant()}"
            : null;
    }

    internal static IReadOnlyList<TeamLabRuntimeAssetCapabilityModel> Capabilities(
        TeamLabRuntimeAsset asset, TeamLabRuntimeStatus runtimeStatus, int runtimeGeneration,
        string operatingSystem,
        ImageRuntimeAccessSummary? configuration)
    {
        var available = asset.WorkerNodeId is not null &&
            !string.IsNullOrWhiteSpace(asset.RuntimeResourceId);
        TeamLabRuntimeAssetCapabilityModel Unsupported(string kind, string reason) =>
            new(kind, "unsupported", reason);
        TeamLabRuntimeAssetCapabilityModel Ready(string kind, bool usable, string reason) =>
            new(kind, usable && available ? "configured-unverified" : "currently-unavailable", reason);
        TeamLabRuntimeAssetCapabilityModel Configured(string kind, TeamLabRemoteProtocol protocol,
            bool usable) =>
            configuration is not { RemoteConfigured: true } ||
            !string.Equals(configuration.RemoteProtocol, protocol.ToString(),
                StringComparison.OrdinalIgnoreCase)
                ? new(kind, "unconfigured", "需在环境模板中配置运维入口", asset.SourceTemplateId)
                : !usable || !available
                    ? new(kind, "currently-unavailable", "资产当前不可用", asset.SourceTemplateId)
                    : new(kind, "configured-unverified", "模板已配置，尚未验证来宾服务与网络连通", asset.SourceTemplateId);

        var running = IsRunning(asset, runtimeStatus, runtimeGeneration);
        var vmAccessReady = running && !string.IsNullOrWhiteSpace(asset.NativeIdentity) &&
            !string.IsNullOrWhiteSpace(asset.IpAddress);
        if (asset.Kind == TeamLabResourceKind.Docker)
            return [
                Unsupported("console", "Docker 不使用虚拟机控制台"),
                Unsupported("rdp", "Docker 不提供 RDP 运维入口"),
                Unsupported("ssh", "Docker 终端由平台提供"),
                Unsupported("sftp", "Docker 文件操作由平台提供"),
                Ready("terminal", running, "平台终端需运行中的容器"),
                Ready("files", running, "容器文件操作需运行中的容器")
            ];
        if (asset.Kind != TeamLabResourceKind.Vm) return [];
        var consoleAvailable = runtimeStatus is TeamLabRuntimeStatus.Running or
            TeamLabRuntimeStatus.Deploying or TeamLabRuntimeStatus.Probing or
            TeamLabRuntimeStatus.Paused or TeamLabRuntimeStatus.Failed;
        var console = Ready("console", consoleAvailable &&
            !string.IsNullOrWhiteSpace(asset.NativeIdentity), "虚拟机控制台按需创建，连接尚未验证");
        if (operatingSystem == "windows")
            return [console, Configured("rdp", TeamLabRemoteProtocol.Rdp, vmAccessReady),
                Unsupported("ssh", "Windows VM 不提供 SSH 运维入口"),
                Unsupported("sftp", "Windows VM 文件管理不受支持"),
                Unsupported("terminal", "VM 不使用容器终端"),
                Unsupported("files", "Windows VM 文件管理不受支持")];
        if (operatingSystem == "linux")
            return [console, Unsupported("rdp", "Linux VM 不提供 RDP 运维入口"),
                Configured("ssh", TeamLabRemoteProtocol.Ssh, vmAccessReady),
                Configured("sftp", TeamLabRemoteProtocol.Ssh, vmAccessReady),
                Unsupported("terminal", "VM 不使用容器终端"),
                Configured("files", TeamLabRemoteProtocol.Ssh, vmAccessReady)];
        return [console, Ready("rdp", false, "无法确认虚拟机操作系统"),
            Ready("ssh", false, "无法确认虚拟机操作系统"),
            Ready("sftp", false, "无法确认虚拟机操作系统"),
            Unsupported("terminal", "VM 不使用容器终端"),
            Ready("files", false, "无法确认虚拟机操作系统")];
    }
}
