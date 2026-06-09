using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Concurrency;

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
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<FleetManager> _logger;

    public FleetManager(
        WeightedScheduler scheduler,
        QueueManager queue,
        INodeRepository nodeRepo,
        AppDbContext context,
        IDistributedLockService lockService,
        ILogger<FleetManager> logger)
    {
        _scheduler = scheduler;
        _queue = queue;
        _nodeRepo = nodeRepo;
        _context = context;
        _lockService = lockService;
        _logger = logger;
    }

    public async Task<Guid?> TryScheduleAsync(DeploymentTarget target, CancellationToken token,
        bool queueWhenNoNode = true)
    {
        var result = await TryScheduleWithTargetAsync(target, token, queueWhenNoNode);
        return result.NodeId;
    }

    public async Task<FleetScheduleResult> TryScheduleWithTargetAsync(DeploymentTarget target, CancellationToken token,
        bool queueWhenNoNode = true)
    {
        using var scheduleLock = await _lockService.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10));
        var capability = GetRequiredCapability(target.Type);
        var nodeId = await _scheduler.SelectOptimalNodeAsync(capability, token);

        if (nodeId is null)
        {
            if (!queueWhenNoNode)
            {
                _logger.LogInformation("Deployment {Id} ({Type}) was not queued - no node available",
                    target.Id, target.Type);
                return FleetScheduleResult.NotScheduled("No schedulable node available");
            }

            target.Status = TargetStatus.Pending;
            target.TargetNodeId = null;
            target.ErrorMessage = "Waiting for a schedulable node";
            _context.DeploymentTargets.Add(target);
            await _context.SaveChangesAsync(token);
            await _queue.EnqueueAsync(target, token);
            _logger.LogInformation("Deployment {Id} ({Type}) queued - no node available",
                target.Id, target.Type);
            return FleetScheduleResult.Queued(target, "No schedulable node available");
        }

        var node = await _nodeRepo.GetNodeByIdAsync(nodeId.Value, token);
        if (node is null)
        {
            _logger.LogWarning("Selected node {NodeId} disappeared before deployment {Id} could be assigned",
                nodeId.Value, target.Id);
            return FleetScheduleResult.NotScheduled("Selected node is no longer available");
        }

        ReserveCapacity(node, capability);
        target.TargetNodeId = nodeId.Value;
        target.Status = TargetStatus.Running;
        target.ErrorMessage = null;
        _context.DeploymentTargets.Add(target);
        await _context.SaveChangesAsync(token);

        return FleetScheduleResult.Scheduled(nodeId.Value, node, target);
    }

    public async Task<List<WorkerNode>> GetAllNodesAsync(CancellationToken token) =>
        await _nodeRepo.GetAllNodesAsync(token);

    internal static NodeCapability GetRequiredCapability(TargetType type) =>
        type == TargetType.Vm ? NodeCapability.Kvm : NodeCapability.Docker;

    internal static void ReserveCapacity(WorkerNode node, NodeCapability capability)
    {
        if ((capability & NodeCapability.Docker) == NodeCapability.Docker)
            node.CurrentContainers++;
        if ((capability & NodeCapability.Kvm) == NodeCapability.Kvm)
            node.CurrentVms++;
    }

    internal static void ReleaseCapacity(WorkerNode node, NodeCapability capability)
    {
        if ((capability & NodeCapability.Docker) == NodeCapability.Docker)
            node.CurrentContainers = Math.Max(0, node.CurrentContainers - 1);
        if ((capability & NodeCapability.Kvm) == NodeCapability.Kvm)
            node.CurrentVms = Math.Max(0, node.CurrentVms - 1);
    }
}

public sealed record FleetScheduleResult(
    Guid? NodeId,
    WorkerNode? Node,
    DeploymentTarget? Target,
    bool IsQueued,
    string? Reason)
{
    public static FleetScheduleResult Scheduled(Guid nodeId, WorkerNode? node, DeploymentTarget target) =>
        new(nodeId, node, target, false, null);

    public static FleetScheduleResult Queued(DeploymentTarget target, string reason) =>
        new(null, null, target, true, reason);

    public static FleetScheduleResult NotScheduled(string reason) =>
        new(null, null, null, false, reason);
}
