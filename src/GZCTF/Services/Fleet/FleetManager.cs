using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;

namespace GZCTF.Services.Fleet;

/// <summary>
/// Main entry point for fleet operations — delegates to WeightedScheduler + QueueManager.
/// </summary>
public class FleetManager
{
    private readonly WeightedScheduler _scheduler;
    private readonly QueueManager _queue;
    private readonly INodeRepository _nodeRepo;
    private readonly AppDbContext _context;
    private readonly ILogger<FleetManager> _logger;

    public FleetManager(
        WeightedScheduler scheduler,
        QueueManager queue,
        INodeRepository nodeRepo,
        AppDbContext context,
        ILogger<FleetManager> logger)
    {
        _scheduler = scheduler;
        _queue = queue;
        _nodeRepo = nodeRepo;
        _context = context;
        _logger = logger;
    }

    public async Task<Guid?> TryScheduleAsync(DeploymentTarget target, CancellationToken token)
    {
        var capability = target.Type == TargetType.Vm ? NodeCapability.Kvm : NodeCapability.Docker;
        var nodeId = await _scheduler.SelectOptimalNodeAsync(capability, token);

        if (nodeId is null)
        {
            target.Status = TargetStatus.Pending;
            target.TargetNodeId = null;
            _context.DeploymentTargets.Add(target);
            await _context.SaveChangesAsync(token);
            await _queue.EnqueueAsync(target, token);
            _logger.LogInformation("Deployment {Id} ({Type}) queued - no node available",
                target.Id, target.Type);
            return null;
        }

        target.TargetNodeId = nodeId.Value;
        return nodeId;
    }

    public async Task<List<WorkerNode>> GetAllNodesAsync(CancellationToken token) =>
        await _nodeRepo.GetAllNodesAsync(token);
}
