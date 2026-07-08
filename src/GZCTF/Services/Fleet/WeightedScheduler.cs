using System.Globalization;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;

namespace GZCTF.Services.Fleet;

public class WeightedScheduler
{
    private const float MinimumSchedulableScore = 200f;
    private readonly INodeRepository _nodeRepo;
    private readonly ILogger<WeightedScheduler> _logger;

    public WeightedScheduler(INodeRepository nodeRepo, ILogger<WeightedScheduler> logger)
    { _nodeRepo = nodeRepo; _logger = logger; }

    public async Task<Guid?> SelectOptimalNodeAsync(NodeCapability required, CancellationToken token)
    {
        var nodes = await _nodeRepo.GetOnlineNodesAsync(token);
        if (nodes.Count == 0) return null;

        var best = SelectOptimalNode(nodes, required);
        return best?.Id;
    }

    public static WorkerNode? SelectOptimalNode(IEnumerable<WorkerNode> nodes, NodeCapability required)
    {
        var scored = nodes
            .Where(n => CanHost(n, required))
            .Select(n => new { Node = n, Score = CalculateScore(n) })
            .OrderByDescending(x => x.Score).ToList();

        var best = scored.FirstOrDefault();
        if (best is null || best.Score < MinimumSchedulableScore) return null;
        return best.Node;
    }

    public static WorkerNode? SelectOptimalTeamLabNode(IEnumerable<WorkerNode> nodes)
    {
        var scored = nodes
            .Where(CanHostTeamLab)
            .Select(n => new { Node = n, Score = CalculateScore(n) })
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored.FirstOrDefault();
        if (best is null || best.Score < MinimumSchedulableScore) return null;
        return best.Node;
    }

    internal static bool CanHost(WorkerNode node, NodeCapability required) =>
        GetUnschedulableReason(node, required) is null;

    internal static bool CanHostTeamLab(WorkerNode node) =>
        GetTeamLabUnschedulableReason(node) is null;

    internal static bool CanHostTeamLabFabric(WorkerNode node) =>
        GetTeamLabFabricUnschedulableReason(node) is null;

    internal static bool CanHostTeamLabDocker(WorkerNode node) =>
        GetTeamLabAssetHostUnschedulableReason(node, requiresDocker: true, requiresVm: false) is null;

    internal static bool CanHostTeamLabVm(WorkerNode node) =>
        GetTeamLabAssetHostUnschedulableReason(node, requiresDocker: false, requiresVm: true) is null;

    internal static string? GetTeamLabUnschedulableReason(WorkerNode node)
    {
        var baseReason = GetUnschedulableReason(node, NodeCapability.Docker | NodeCapability.Kvm);
        if (baseReason is not null)
            return baseReason;

        return GetTeamLabDataPlaneUnschedulableReason(node);
    }

    internal static string? GetTeamLabAssetHostUnschedulableReason(
        WorkerNode node,
        bool requiresDocker,
        bool requiresVm)
    {
        var required = NodeCapability.None;
        if (requiresDocker)
            required |= NodeCapability.Docker;
        if (requiresVm)
            required |= NodeCapability.Kvm;

        var baseReason = GetUnschedulableReason(node, required);
        if (baseReason is not null)
            return baseReason;

        return GetTeamLabDataPlaneUnschedulableReason(node);
    }

    internal static string? GetTeamLabFabricUnschedulableReason(WorkerNode node)
    {
        var baseReason = GetUnschedulableReason(node, NodeCapability.None);
        if (baseReason is not null)
            return baseReason;

        return GetTeamLabDataPlaneUnschedulableReason(node);
    }

    static string? GetTeamLabDataPlaneUnschedulableReason(WorkerNode node)
    {
        if (!node.TeamLabNetworkEnabled)
            return "TeamLab network is not enabled";

        if (node.TeamLabTunnelStatus != TeamLabTunnelStatus.Healthy)
            return "TeamLab tunnel is not healthy";

        if (string.IsNullOrWhiteSpace(node.TeamLabTunnelIp))
            return "TeamLab tunnel IP is not configured";

        if (!IsValidIpv4Address(node.TeamLabTunnelIp.Trim()))
            return "TeamLab tunnel IP is invalid";

        if (node.TeamLabProtocolVersion < 3)
            return "TeamLab Agent protocol is incompatible; TeamLab Fabric namespace uplink requires protocol v3";

        if (string.IsNullOrWhiteSpace(node.TeamLabAgentVersion))
            return "TeamLab Agent version is not reported";

        return null;
    }

    internal static string? GetUnschedulableReason(WorkerNode node, NodeCapability required)
    {
        if (node.GetEffectiveStatus(DateTimeOffset.UtcNow) != NodeStatus.Online)
            return "Node is offline or heartbeat is stale";

        if (!node.IsSchedulable)
            return "Node scheduling is disabled";

        if ((node.Capabilities & required) != required)
            return $"Node lacks required capability: {required}";

        if (!float.IsFinite(node.CpuLoad) || !float.IsFinite(node.MemoryLoad)
            || node.CpuLoad < 0 || node.CpuLoad > 1
            || node.MemoryLoad < 0 || node.MemoryLoad > 1
            || node.MaxContainers < 0 || node.MaxVms < 0
            || node.CurrentContainers < 0 || node.CurrentVms < 0
            || node.ReservedContainers < 0 || node.ReservedVms < 0)
            return "Node capacity metrics are invalid";

        var hasCapacity = required switch
        {
            NodeCapability.Docker => node.AllocatedContainers < node.MaxContainers,
            NodeCapability.Kvm => node.AllocatedVms < node.MaxVms,
            NodeCapability.Docker | NodeCapability.Kvm =>
                node.AllocatedContainers < node.MaxContainers && node.AllocatedVms < node.MaxVms,
            _ => true
        };

        return hasCapacity ? null : $"Node capacity exhausted for {required}";
    }

    private static float CalculateScore(WorkerNode n) =>
        1000f * (1 - Math.Clamp(n.CpuLoad, 0f, 1f))
        + 500f * (1 - Math.Clamp(n.MemoryLoad, 0f, 1f))
        + 200f * (1 - (float)n.AllocatedContainers / Math.Max(n.MaxContainers, 1))
        + 200f * (1 - (float)n.AllocatedVms / Math.Max(n.MaxVms, 1));

    private static bool IsValidIpv4Address(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part =>
            part.Length > 0 &&
            int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet) &&
            octet is >= 0 and <= 255);
    }
}
