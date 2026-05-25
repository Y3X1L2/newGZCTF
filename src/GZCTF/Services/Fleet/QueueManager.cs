using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class QueueManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueManager> _logger;

    public QueueManager(IServiceScopeFactory scopeFactory, ILogger<QueueManager> logger)
    {
        _scopeFactory = scopeFactory;
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
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<WeightedScheduler>();

        var pending = await context.DeploymentTargets
            .Where(t => t.Status == TargetStatus.Pending && t.TargetNodeId == null)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(token);

        int processed = 0;
        foreach (var target in pending)
        {
            if (token.IsCancellationRequested)
                break;

            var required = target.Type == TargetType.Vm ? NodeCapability.Kvm : NodeCapability.Docker;
            var nodeId = await scheduler.SelectOptimalNodeAsync(required, token);

            if (!nodeId.HasValue)
            {
                _logger.LogDebug("Still no node available for queued deployment {Id} ({Type})",
                    target.Id, target.Type);
                break; // No more nodes available for this capability, stop processing
            }

            target.TargetNodeId = nodeId.Value;
            target.Status = TargetStatus.Running;
            processed++;

            _logger.LogInformation("Queued deployment {Id} ({Type}) assigned to node {NodeId}",
                target.Id, target.Type, nodeId.Value);
        }

        if (processed > 0)
            await context.SaveChangesAsync(token);

        return processed;
    }
}
