using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;

namespace GZCTF.Services.Fleet;

public class WeightedScheduler
{
    private readonly INodeRepository _nodeRepo;
    private readonly ILogger<WeightedScheduler> _logger;

    public WeightedScheduler(INodeRepository nodeRepo, ILogger<WeightedScheduler> logger)
    { _nodeRepo = nodeRepo; _logger = logger; }

    public async Task<Guid?> SelectOptimalNodeAsync(NodeCapability required, CancellationToken token)
    {
        var nodes = await _nodeRepo.GetOnlineNodesAsync(token);
        if (nodes.Count == 0) return null;

        var scored = nodes
            .Where(n => (n.Capabilities & required) == required && n.IsSchedulable)
            .Select(n => new { Node = n, Score = CalculateScore(n) })
            .OrderByDescending(x => x.Score).ToList();

        var best = scored.FirstOrDefault();
        if (best is null || best.Score < 200) return null;
        return best.Node.Id;
    }

    private static float CalculateScore(WorkerNode n) =>
        1000f * (1 - n.CpuLoad) + 500f * (1 - n.MemoryLoad)
        + 200f * (1 - (float)n.CurrentContainers / Math.Max(n.MaxContainers, 1))
        + 200f * (1 - (float)n.CurrentVms / Math.Max(n.MaxVms, 1));
}
