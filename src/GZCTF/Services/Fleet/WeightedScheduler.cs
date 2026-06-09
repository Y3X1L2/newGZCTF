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

    internal static bool CanHost(WorkerNode node, NodeCapability required) =>
        GetUnschedulableReason(node, required) is null;

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
            || node.CurrentContainers < 0 || node.CurrentVms < 0)
            return "Node capacity metrics are invalid";

        var hasCapacity = required switch
        {
            NodeCapability.Docker => node.CurrentContainers < node.MaxContainers,
            NodeCapability.Kvm => node.CurrentVms < node.MaxVms,
            NodeCapability.Docker | NodeCapability.Kvm =>
                node.CurrentContainers < node.MaxContainers && node.CurrentVms < node.MaxVms,
            _ => true
        };

        return hasCapacity ? null : $"Node capacity exhausted for {required}";
    }

    private static float CalculateScore(WorkerNode n) =>
        1000f * (1 - Math.Clamp(n.CpuLoad, 0f, 1f))
        + 500f * (1 - Math.Clamp(n.MemoryLoad, 0f, 1f))
        + 200f * (1 - (float)n.CurrentContainers / Math.Max(n.MaxContainers, 1))
        + 200f * (1 - (float)n.CurrentVms / Math.Max(n.MaxVms, 1));
}
