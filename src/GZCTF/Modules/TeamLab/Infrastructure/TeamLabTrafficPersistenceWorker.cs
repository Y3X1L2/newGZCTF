using GZCTF.Modules.TeamLab.Application;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabTrafficPersistenceWorker(
    IServiceScopeFactory scopeFactory,
    ITeamLabTrafficIngestor ingestor,
    ILogger<TeamLabTrafficPersistenceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan MaximumBatchDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan BatchPollInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan ReclaimIdle = TimeSpan.FromSeconds(30);
    private readonly string _consumerName = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        CollectAsync(stoppingToken),
        PersistAsync(stoppingToken));

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TeamLabTrafficApplicationService>()
                    .CollectAvailableFlowsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "TeamLab 流量采集周期执行失败");
            }
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var failureDelay = MaximumBatchDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            TeamLabTrafficReadBatch batch = TeamLabTrafficReadBatch.Empty;
            try
            {
                batch = await ReadAccumulatedBatchAsync(cancellationToken);
                if (batch.Messages.Count == 0)
                    continue;

                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PostgresTeamLabTrafficBatchWriter>()
                    .WriteAsync(batch.Messages.Select(item => item.Envelope).ToArray(), cancellationToken);
                await ingestor.AcknowledgeAsync(
                    batch.Messages.Where(item => item.StreamId is not null).Select(item => item.StreamId!).ToArray(),
                    batch.Messages.Where(item => item.LocalSequence.HasValue).Select(item => item.LocalSequence!.Value).ToArray(),
                    cancellationToken);
                failureDelay = MaximumBatchDelay;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "TeamLab 流量持久化批次失败：count={Count}", batch.Messages.Count);
                await Task.Delay(failureDelay, cancellationToken);
                failureDelay = TimeSpan.FromMilliseconds(Math.Min(failureDelay.TotalMilliseconds * 2, 5000));
            }
        }
    }

    private async Task<TeamLabTrafficReadBatch> ReadAccumulatedBatchAsync(CancellationToken cancellationToken)
    {
        var messages = new List<TeamLabTrafficIngestMessage>(TeamLabTrafficIngestionLimits.MaxBatchSamples);
        var deadline = DateTimeOffset.UtcNow + MaximumBatchDelay;
        while (messages.Count < TeamLabTrafficIngestionLimits.MaxBatchSamples)
        {
            var read = await ingestor.ReadAsync(
                _consumerName,
                Math.Min(250, TeamLabTrafficIngestionLimits.MaxBatchSamples - messages.Count),
                ReclaimIdle,
                cancellationToken);
            messages.AddRange(read.Messages);
            if (messages.Count >= TeamLabTrafficIngestionLimits.MaxBatchSamples)
                break;

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;
            await Task.Delay(remaining < BatchPollInterval ? remaining : BatchPollInterval, cancellationToken);
        }

        return messages.Count == 0 ? TeamLabTrafficReadBatch.Empty : new TeamLabTrafficReadBatch(messages);
    }
}
