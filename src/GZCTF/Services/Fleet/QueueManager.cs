using GZCTF.Models.Data;
using GZCTF.Services.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class QueueManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<QueueManager> _logger;

    public QueueManager(IServiceScopeFactory scopeFactory, IDistributedLockService lockService,
        ILogger<QueueManager> logger)
    {
        _scopeFactory = scopeFactory;
        _lockService = lockService;
        _logger = logger;
    }

    public Task EnqueueAsync(DeploymentTarget target, CancellationToken token = default)
    {
        _logger.LogInformation("Deployment {Id} ({Type}) queued - no schedulable node available",
            target.Id, target.Type);
        return Task.CompletedTask;
    }

    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        using var scheduleLock = await _lockService.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10));
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await context.DeploymentTargets
            .Where(t => t.Status == TargetStatus.Pending && t.TargetNodeId == null)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(token);
        var cutoff = DateTimeOffset.UtcNow - WorkerNode.DefaultHeartbeatTimeout;
        var nodes = await context.WorkerNodes
            .Where(n => n.Status == NodeStatus.Online
                && (n.IsLocal || (n.LastHeartbeat.HasValue && n.LastHeartbeat >= cutoff)))
            .ToListAsync(token);

        int processed = 0;
        foreach (var target in pending)
        {
            if (token.IsCancellationRequested)
                break;

            var required = FleetManager.GetRequiredCapability(target.Type);
            var node = WeightedScheduler.SelectOptimalNode(nodes, required);

            if (node is null)
            {
                _logger.LogDebug("Still no node available for queued deployment {Id} ({Type})",
                    target.Id, target.Type);
                continue;
            }

            target.TargetNodeId = node.Id;
            target.Status = TargetStatus.Assigned;
            target.ErrorMessage = null;
            processed++;

            _logger.LogInformation(
                "Queued deployment {Id} ({Type}) assigned to node {NodeId}; creation is deferred to a target-specific worker.",
                target.Id, target.Type, node.Id);
        }

        if (processed > 0)
            await context.SaveChangesAsync(token);

        return processed;
    }
}
