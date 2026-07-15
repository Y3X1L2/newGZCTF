using System.Diagnostics.Metrics;
using GZCTF.Modules.Runtime.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Telemetry;

public sealed class RuntimeTelemetrySnapshotWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RuntimeTelemetrySnapshotWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await CaptureAsync(stoppingToken);
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CaptureAsync(CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var liveStateStore = scope.ServiceProvider.GetRequiredService<INodeLiveStateStore>();
            var queue = await context.DeploymentQueueTickets.AsNoTracking()
                .GroupBy(ticket => new { ticket.Kind, ticket.Status })
                .Select(group => new { group.Key.Kind, group.Key.Status, Count = group.LongCount() })
                .ToArrayAsync(token);
            PlatformTelemetry.UpdateQueueDepth(queue.Select(item => new Measurement<long>(
                item.Count,
                new KeyValuePair<string, object?>("workload", item.Kind.ToString()),
                new KeyValuePair<string, object?>("status", item.Status.ToString()))));

            var nodes = await context.WorkerNodes.AsNoTracking()
                .Select(node => new
                {
                    node.Id,
                    node.Status,
                    node.IsSchedulable,
                    node.CpuLoad,
                    node.MemoryLoad,
                    node.LastHeartbeat,
                    node.IsLocal
                })
                .ToArrayAsync(token);
            var live = await liveStateStore.GetManyAsync(nodes.Select(node => node.Id).ToArray(), token);
            var now = DateTimeOffset.UtcNow;
            var online = 0L;
            var schedulable = 0L;
            var overloaded = 0L;
            foreach (var node in nodes)
            {
                var effectiveOnline = node.Status == NodeStatus.Online &&
                                      (node.IsLocal || node.LastHeartbeat >=
                                          now - WorkerNode.DefaultHeartbeatTimeout);
                if (effectiveOnline)
                    online++;
                if (effectiveOnline && node.IsSchedulable)
                    schedulable++;
                var cpu = live.TryGetValue(node.Id, out var state) && state.IsFresh(now, liveStateStore.FreshnessTtl)
                    ? state.CpuLoad
                    : node.CpuLoad;
                var memory = live.TryGetValue(node.Id, out state) && state.IsFresh(now, liveStateStore.FreshnessTtl)
                    ? state.MemoryLoad
                    : node.MemoryLoad;
                if (effectiveOnline && (cpu >= 0.9f || memory >= 0.9f))
                    overloaded++;
            }

            PlatformTelemetry.UpdateNodeSummary(
            [
                new Measurement<long>(nodes.LongLength,
                    new KeyValuePair<string, object?>("state", "total")),
                new Measurement<long>(online,
                    new KeyValuePair<string, object?>("state", "online")),
                new Measurement<long>(schedulable,
                    new KeyValuePair<string, object?>("state", "schedulable")),
                new Measurement<long>(overloaded,
                    new KeyValuePair<string, object?>("state", "overloaded"))
            ]);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to refresh runtime telemetry snapshot.");
        }
    }
}
