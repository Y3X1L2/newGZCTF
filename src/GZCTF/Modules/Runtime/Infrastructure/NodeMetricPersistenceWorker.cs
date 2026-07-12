using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class NodeMetricPersistenceWorker(
    IServiceScopeFactory scopeFactory,
    RedisNodeLiveStateStore redisStore,
    PostgresNodeLiveStateFallback fallback,
    ILogger<NodeMetricPersistenceWorker> logger) : BackgroundService
{
    internal const int MaximumBatchSize = 500;
    internal static readonly TimeSpan MaximumBatchDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var localStates = fallback.Drain(MaximumBatchSize);
            var redisEntries = localStates.Count < MaximumBatchSize
                ? await redisStore.ReadBatchAsync(MaximumBatchSize - localStates.Count, stoppingToken)
                : [];
            var states = localStates.Concat(redisEntries.Select(entry => entry.State)).ToArray();

            if (states.Length == 0)
            {
                await Task.Delay(MaximumBatchDelay, stoppingToken);
                continue;
            }

            try
            {
                await PersistBatchAsync(scopeFactory, states, stoppingToken);
                fallback.MarkPersisted(localStates);
                await redisStore.AcknowledgeAsync(
                    redisEntries.Select(entry => entry.EntryId).ToArray(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                fallback.Requeue(localStates);
                throw;
            }
            catch (Exception exception)
            {
                fallback.Requeue(localStates);
                logger.LogError(exception,
                    "Failed to persist node metric batch containing {MetricCount} samples", states.Length);
                await Task.Delay(MaximumBatchDelay, stoppingToken);
            }

            if (states.Length < MaximumBatchSize)
                await Task.Delay(MaximumBatchDelay, stoppingToken);
        }
    }

    internal static async Task PersistBatchAsync(IServiceScopeFactory scopeFactory,
        IReadOnlyCollection<NodeLiveState> input,
        CancellationToken cancellationToken)
    {
        if (input.Count == 0)
            return;

        var states = input
            .GroupBy(state => new { state.WorkerNodeId, state.Sequence })
            .Select(group => group.OrderByDescending(state => state.ReceivedAt).First())
            .OrderBy(state => state.Sequence)
            .ToArray();
        var nodeIds = states.Select(state => state.WorkerNodeId).Distinct().ToArray();

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var nodes = await context.WorkerNodes
            .Where(node => nodeIds.Contains(node.Id))
            .ToDictionaryAsync(node => node.Id, cancellationToken);
        var knownNodeIds = nodes.Keys.ToHashSet();
        var metricGroups = states
            .Where(state => knownNodeIds.Contains(state.WorkerNodeId))
            .GroupBy(state => new
            {
                state.WorkerNodeId,
                WindowStart = TruncateToMinute(state.ReceivedAt)
            })
            .ToArray();

        if (metricGroups.Length > 0)
        {
            var minimumWindow = metricGroups.Min(group => group.Key.WindowStart);
            var maximumWindow = metricGroups.Max(group => group.Key.WindowStart);
            var existingSamples = await context.WorkerNodeMetricSamples
                .Where(sample => nodeIds.Contains(sample.WorkerNodeId) &&
                                 sample.WindowStart >= minimumWindow &&
                                 sample.WindowStart <= maximumWindow)
                .ToDictionaryAsync(
                    sample => (sample.WorkerNodeId, sample.WindowStart),
                    cancellationToken);

            foreach (var group in metricGroups)
            {
                var key = (group.Key.WorkerNodeId, group.Key.WindowStart);
                if (!existingSamples.TryGetValue(key, out var sample))
                {
                    sample = new WorkerNodeMetricSample
                    {
                        WorkerNodeId = group.Key.WorkerNodeId,
                        WindowStart = group.Key.WindowStart
                    };
                    context.WorkerNodeMetricSamples.Add(sample);
                    existingSamples[key] = sample;
                }

                foreach (var state in group.OrderBy(state => state.Sequence))
                {
                    if (sample.SampleCount > 0 && state.Sequence <= sample.LastSequence)
                        continue;
                    Accumulate(sample, state);
                }
            }
        }

        foreach (var nodeGroup in states.GroupBy(state => state.WorkerNodeId))
        {
            if (!nodes.TryGetValue(nodeGroup.Key, out var node))
                continue;

            var latest = nodeGroup.OrderByDescending(state => state.Sequence).First();
            if (latest.Sequence <= node.LiveMetricSequence)
                continue;

            node.LiveMetricSequence = latest.Sequence;
            node.LiveMetricObservedAt = latest.ObservedAt;
            node.LiveMetricReceivedAt = latest.ReceivedAt;
            node.CpuLoad = latest.CpuLoad;
            node.MemoryLoad = latest.MemoryLoad;
            node.CurrentContainers = latest.CurrentContainers;
            node.CurrentVms = latest.CurrentVms;
            node.UsedPorts = latest.UsedPorts;
            node.LastHeartbeat = latest.ReceivedAt;
            node.Status = NodeStatus.Online;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        foreach (var nodeId in nodeIds)
            await capacity.ReconcileReservedAsync(nodeId, cancellationToken);
    }

    private static void Accumulate(WorkerNodeMetricSample sample, NodeLiveState state)
    {
        if (sample.SampleCount == 0)
        {
            sample.SampleCount = 1;
            sample.AverageCpuLoad = sample.MinimumCpuLoad = sample.MaximumCpuLoad = state.CpuLoad;
            sample.AverageMemoryLoad = sample.MinimumMemoryLoad = sample.MaximumMemoryLoad = state.MemoryLoad;
            sample.AverageContainers = sample.MaximumContainers = state.CurrentContainers;
            sample.AverageVms = sample.MaximumVms = state.CurrentVms;
            sample.AverageUsedPorts = sample.MaximumUsedPorts = state.UsedPorts;
            sample.FirstSequence = sample.LastSequence = state.Sequence;
            sample.FirstReceivedAt = sample.LastReceivedAt = state.ReceivedAt;
            return;
        }

        var nextCount = sample.SampleCount + 1;
        sample.AverageCpuLoad = WeightedAverage(sample.AverageCpuLoad, sample.SampleCount, state.CpuLoad, nextCount);
        sample.MinimumCpuLoad = Math.Min(sample.MinimumCpuLoad, state.CpuLoad);
        sample.MaximumCpuLoad = Math.Max(sample.MaximumCpuLoad, state.CpuLoad);
        sample.AverageMemoryLoad = WeightedAverage(
            sample.AverageMemoryLoad, sample.SampleCount, state.MemoryLoad, nextCount);
        sample.MinimumMemoryLoad = Math.Min(sample.MinimumMemoryLoad, state.MemoryLoad);
        sample.MaximumMemoryLoad = Math.Max(sample.MaximumMemoryLoad, state.MemoryLoad);
        sample.AverageContainers = WeightedAverage(
            sample.AverageContainers, sample.SampleCount, state.CurrentContainers, nextCount);
        sample.MaximumContainers = Math.Max(sample.MaximumContainers, state.CurrentContainers);
        sample.AverageVms = WeightedAverage(sample.AverageVms, sample.SampleCount, state.CurrentVms, nextCount);
        sample.MaximumVms = Math.Max(sample.MaximumVms, state.CurrentVms);
        sample.AverageUsedPorts = WeightedAverage(
            sample.AverageUsedPorts, sample.SampleCount, state.UsedPorts, nextCount);
        sample.MaximumUsedPorts = Math.Max(sample.MaximumUsedPorts, state.UsedPorts);
        sample.SampleCount = nextCount;
        sample.LastSequence = state.Sequence;
        sample.LastReceivedAt = state.ReceivedAt;
    }

    private static float WeightedAverage(float current, int currentCount, float value, int nextCount) =>
        (current * currentCount + value) / nextCount;

    private static double WeightedAverage(double current, int currentCount, double value, int nextCount) =>
        (current * currentCount + value) / nextCount;

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}
