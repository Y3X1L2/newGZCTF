using System.Diagnostics;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class DataRetentionExecutor(
    AppDbContext context,
    DataRetentionPolicyCatalog catalog,
    PostgresPartitionManager partitions,
    OperationalAggregationService aggregation,
    TerminalHistoryCleaner cleaner,
    DataGovernanceMetrics metrics,
    ILogger<DataRetentionExecutor> logger)
{
    public async Task ExecuteAsync(string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await partitions.EnsureFuturePartitionsAsync(now, cancellationToken);
        await AggregateLogsAsync(leaseOwner, now, cancellationToken);
        await AggregateFlowsAsync(leaseOwner, now, cancellationToken);
        await AggregateDeploymentsAsync(leaseOwner, now, cancellationToken);

        await DropExpiredPartitionAsync("system-log", leaseOwner, now, cancellationToken);
        await DropExpiredPartitionAsync("teamlab-flow", leaseOwner, now, cancellationToken);

        await CleanAggregateAsync("system-log", leaseOwner, now,
            cleaner.CleanLogAggregatesAsync, cancellationToken);
        await CleanAggregateAsync("deployment-ticket", leaseOwner, now,
            cleaner.CleanDeploymentAggregatesAsync, cancellationToken);

        await CleanAsync("deployment-ticket", leaseOwner, now,
            cleaner.CleanDeploymentTicketsAsync, cancellationToken);
        await CleanAsync("operational-event", leaseOwner, now,
            cleaner.CleanOperationalEventsAsync, cancellationToken);
        await CleanAsync("api-operation", leaseOwner, now,
            cleaner.CleanApiOperationsAsync, cancellationToken);
        await CleanAsync("teamlab-event", leaseOwner, now,
            cleaner.CleanTeamLabEventsAsync, cancellationToken);
        await CleanAsync("teamlab-flow-aggregate", leaseOwner, now,
            cleaner.CleanFlowAggregatesAsync, cancellationToken);
        await CleanAsync("governance-run", leaseOwner, now,
            cleaner.CleanGovernanceRunsAsync, cancellationToken);
        await CleanAsync("worker-node-metric", leaseOwner, now,
            cleaner.CleanWorkerNodeMetricsAsync, cancellationToken);
    }

    private async Task AggregateLogsAsync(string leaseOwner, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var policy = catalog.GetRequired("system-log");
        var end = FloorHour(now);
        var earliest = now - policy.RawRetention!.Value;
        var latest = await context.OperationalLogAggregates.MaxAsync(
            item => (DateTimeOffset?)item.BucketStart, cancellationToken);
        var start = latest.HasValue ? latest.Value.AddHours(1) : FloorHour(earliest);
        for (; start < end; start = start.AddHours(1))
        {
            var windowEnd = start.AddHours(1);
            var rowsRead = await context.Logs.LongCountAsync(
                item => item.TimeUtc >= start && item.TimeUtc < windowEnd, cancellationToken);
            await RunAsync("system-log", "aggregate", leaseOwner, windowEnd, rowsRead,
                () => aggregation.AggregateSystemLogsAsync(start, windowEnd, cancellationToken),
                cancellationToken);
        }
    }

    private async Task AggregateFlowsAsync(string leaseOwner, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var policy = catalog.GetRequired("teamlab-flow");
        var end = FloorFiveMinutes(now);
        var earliest = now - policy.RawRetention!.Value;
        var latest = await context.TeamLabTrafficFlowAggregates.MaxAsync(
            item => (DateTimeOffset?)item.BucketStart, cancellationToken);
        var start = latest.HasValue ? latest.Value.AddMinutes(5) : FloorFiveMinutes(earliest);
        for (; start < end; start = start.AddMinutes(5))
        {
            var windowEnd = start.AddMinutes(5);
            var rowsRead = await context.TeamLabTrafficFlows.LongCountAsync(
                item => item.CapturedAt >= start && item.CapturedAt < windowEnd, cancellationToken);
            await RunAsync("teamlab-flow", "aggregate", leaseOwner, windowEnd, rowsRead,
                () => aggregation.AggregateTeamLabFlowsAsync(start, windowEnd, cancellationToken),
                cancellationToken);
        }
    }

    private async Task AggregateDeploymentsAsync(string leaseOwner, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var end = FloorDay(now);
        var latest = await context.DeploymentLifecycleAggregates.MaxAsync(
            item => (DateTimeOffset?)item.BucketStart, cancellationToken);
        var start = latest.HasValue ? latest.Value.AddDays(1) : end.AddDays(-1);
        for (; start < end; start = start.AddDays(1))
        {
            var windowEnd = start.AddDays(1);
            var rowsRead = await context.DeploymentQueueTickets.LongCountAsync(
                item => item.CompletedAt >= start && item.CompletedAt < windowEnd,
                cancellationToken);
            await RunAsync("deployment-ticket", "aggregate", leaseOwner, windowEnd, rowsRead,
                () => aggregation.AggregateDeploymentLifecycleAsync(start, windowEnd, cancellationToken),
                cancellationToken);
        }
    }

    private async Task DropExpiredPartitionAsync(string dataSet, string leaseOwner, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var policy = catalog.GetRequired(dataSet);
        var cutoff = now - policy.RawRetention!.Value;
        var candidates = await partitions.GetExpiredPartitionsAsync(dataSet, cutoff, cancellationToken);
        foreach (var partition in candidates)
            await AggregatePartitionAsync(dataSet, partition, leaseOwner, cancellationToken);

        try
        {
            var result = await partitions.DropExpiredPartitionsAsync(
                dataSet, cutoff, leaseOwner, cancellationToken);
            metrics.RecordRows(dataSet, "drop-partitions", result.RowsDeleted);
        }
        catch (Exception exception)
        {
            metrics.RecordFailure(dataSet, "drop-partitions");
            logger.LogError(exception, "Database partition cleanup failed for {DataSet}.", dataSet);
            throw;
        }
    }

    private async Task AggregatePartitionAsync(
        string dataSet,
        PartitionWindow partition,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (dataSet == "system-log")
        {
            for (var start = partition.Lower; start < partition.Upper; start = start.AddHours(1))
                _ = await aggregation.AggregateSystemLogsAsync(start, start.AddHours(1), cancellationToken);
        }
        else if (dataSet == "teamlab-flow")
        {
            for (var start = partition.Lower; start < partition.Upper; start = start.AddMinutes(5))
                _ = await aggregation.AggregateTeamLabFlowsAsync(start, start.AddMinutes(5), cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Data set '{dataSet}' is not partition aggregated.");
        }

        var rowsRead = dataSet == "system-log"
            ? await ValidateLogAggregateAsync(partition, cancellationToken)
            : await ValidateFlowAggregateAsync(partition, cancellationToken);
        context.DataGovernanceRuns.Add(new DataGovernanceRun
        {
            DataSet = dataSet,
            Operation = "aggregate-partition",
            Status = DataGovernanceRunStatus.Completed,
            LeaseOwner = leaseOwner,
            Cutoff = partition.Upper,
            RowsRead = rowsRead,
            RowsAggregated = rowsRead,
            PartitionName = partition.Name,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<long> ValidateLogAggregateAsync(
        PartitionWindow partition,
        CancellationToken cancellationToken)
    {
        var sourceRows = await context.Logs.LongCountAsync(
            item => item.TimeUtc >= partition.Lower && item.TimeUtc < partition.Upper,
            cancellationToken);
        var aggregateRows = await context.OperationalLogAggregates
            .Where(item => item.BucketStart >= partition.Lower && item.BucketStart < partition.Upper)
            .SumAsync(item => (long?)item.Count, cancellationToken) ?? 0;
        if (sourceRows != aggregateRows)
            throw new InvalidOperationException(
                $"System-log aggregate validation failed for partition {partition.Name}: " +
                $"source rows {sourceRows}, aggregate rows {aggregateRows}.");
        return sourceRows;
    }

    private async Task<long> ValidateFlowAggregateAsync(
        PartitionWindow partition,
        CancellationToken cancellationToken)
    {
        var source = await context.TeamLabTrafficFlows
            .Where(item => item.CapturedAt >= partition.Lower && item.CapturedAt < partition.Upper)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Rows = group.LongCount(),
                Packets = group.Sum(item => item.Packets),
                Bytes = group.Sum(item => item.Bytes)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var target = await context.TeamLabTrafficFlowAggregates
            .Where(item => item.BucketStart >= partition.Lower && item.BucketStart < partition.Upper)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Rows = group.Sum(item => item.FlowCount),
                Packets = group.Sum(item => item.PacketCount),
                Bytes = group.Sum(item => item.Bytes)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var sourceRows = source?.Rows ?? 0;
        if (sourceRows != (target?.Rows ?? 0) ||
            (source?.Packets ?? 0) != (target?.Packets ?? 0) ||
            (source?.Bytes ?? 0) != (target?.Bytes ?? 0))
            throw new InvalidOperationException(
                $"TeamLab flow aggregate validation failed for partition {partition.Name}.");
        return sourceRows;
    }

    private async Task CleanAggregateAsync(
        string sourceDataSet,
        string leaseOwner,
        DateTimeOffset now,
        Func<DateTimeOffset, int, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        var policy = catalog.GetRequired(sourceDataSet);
        if (!policy.AggregateRetention.HasValue)
            return;
        var cutoff = now - policy.AggregateRetention.Value;
        await RunAsync(sourceDataSet, "clean-aggregate", leaseOwner, cutoff, 0,
            () => action(cutoff, policy.DeleteBatchSize, cancellationToken),
            cancellationToken, deleted: true);
    }

    private async Task CleanAsync(
        string dataSet,
        string leaseOwner,
        DateTimeOffset now,
        Func<DateTimeOffset, int, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        var policy = catalog.GetRequired(dataSet);
        var cutoff = now - policy.RawRetention!.Value;
        await RunAsync(dataSet, "clean-terminal", leaseOwner, cutoff, 0,
            () => action(cutoff, policy.DeleteBatchSize, cancellationToken),
            cancellationToken, deleted: true);
    }

    private async Task RunAsync(
        string dataSet,
        string operation,
        string leaseOwner,
        DateTimeOffset cutoff,
        long rowsRead,
        Func<Task<int>> action,
        CancellationToken cancellationToken,
        bool deleted = false)
    {
        var run = new DataGovernanceRun
        {
            DataSet = dataSet,
            Operation = operation,
            Status = DataGovernanceRunStatus.Running,
            LeaseOwner = leaseOwner,
            Cutoff = cutoff,
            RowsRead = rowsRead
        };
        context.DataGovernanceRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var affected = await action();
            run.Status = DataGovernanceRunStatus.Completed;
            run.RowsAggregated = deleted ? 0 : affected;
            run.RowsDeleted = deleted ? affected : 0;
            run.CompletedAt = DateTimeOffset.UtcNow;
            metrics.RecordRows(dataSet, operation, affected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = DataGovernanceRunStatus.Cancelled;
            run.ErrorCode = "cancelled";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            run.Status = DataGovernanceRunStatus.Failed;
            run.ErrorCode = "governance_failed";
            run.ErrorDetail = Trim(exception.Message);
            run.CompletedAt = DateTimeOffset.UtcNow;
            metrics.RecordFailure(dataSet, operation);
            logger.LogError(exception, "Database governance failed for {DataSet}/{Operation}.", dataSet, operation);
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            metrics.RecordDuration(dataSet, operation, stopwatch.Elapsed);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset FloorHour(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset FloorFiveMinutes(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute / 5 * 5, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset FloorDay(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static string Trim(string value) => value.Length <= 2048 ? value : value[..2048];
}
