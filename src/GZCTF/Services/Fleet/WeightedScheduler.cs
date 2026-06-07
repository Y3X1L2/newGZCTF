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
        node.IsSchedulable
        && (node.Capabilities & required) == required
        && required switch
        {
            NodeCapability.Docker => node.CurrentContainers < node.MaxContainers,
            NodeCapability.Kvm => node.CurrentVms < node.MaxVms,
            NodeCapability.Docker | NodeCapability.Kvm =>
                node.CurrentContainers < node.MaxContainers && node.CurrentVms < node.MaxVms,
            _ => true
        };

    private static float CalculateScore(WorkerNode n) =>
        1000f * (1 - Math.Clamp(n.CpuLoad, 0f, 1f))
        + 500f * (1 - Math.Clamp(n.MemoryLoad, 0f, 1f))
        + 200f * (1 - (float)n.CurrentContainers / Math.Max(n.MaxContainers, 1))
        + 200f * (1 - (float)n.CurrentVms / Math.Max(n.MaxVms, 1));
}
