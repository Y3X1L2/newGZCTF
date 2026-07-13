using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public sealed class ImageDistributionWorker(
    IServiceScopeFactory scopeFactory,
    ImageDistributionCoordinator coordinator,
    NodeDispatchLimiter dispatchLimiter,
    ILogger<ImageDistributionWorker> logger) : BackgroundService
{
    const int BatchSize = 32;
    static readonly TimeSpan ClaimDuration = TimeSpan.FromHours(2);
    static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);
    readonly string _claimOwner = $"image:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = await ClaimBatchAsync(stoppingToken);
            if (claimed.Count == 0)
            {
                await coordinator.WaitAsync(PollingInterval, stoppingToken);
                continue;
            }

            await Task.WhenAll(claimed.Select(record => ProcessOneAsync(record, stoppingToken)));
        }
    }

    async Task<IReadOnlyList<ClaimedImageWork>> ClaimBatchAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var candidates = await context.ImageDistributionRecords.AsNoTracking()
            .Where(record =>
                (record.ClaimOwner == null || record.ClaimExpiresAt <= now) &&
                (record.NextAttemptAt == null || record.NextAttemptAt <= now) &&
                (record.Status == ImageDistributionStatus.Pending ||
                 record.Status == ImageDistributionStatus.CleanupPending ||
                 record.Status == ImageDistributionStatus.Failed ||
                 record.Status == ImageDistributionStatus.Pulling))
            .OrderBy(record => record.Operation == ImageDistributionOperation.Cleanup ? 0 : 1)
            .ThenBy(record => record.NextAttemptAt)
            .ThenBy(record => record.CreatedAt)
            .ThenBy(record => record.Id)
            .Take(BatchSize)
            .Select(record => new ClaimedImageWork(record.Id, record.WorkerNodeId, record.ImageType,
                record.Operation))
            .ToArrayAsync(token);

        List<ClaimedImageWork> claimed = [];
        foreach (var candidate in candidates)
        {
            var status = candidate.Operation == ImageDistributionOperation.Cleanup
                ? ImageDistributionStatus.CleanupPending
                : ImageDistributionStatus.Pulling;
            if (context.Database.IsRelational())
            {
                var affected = await context.ImageDistributionRecords
                    .Where(record => record.Id == candidate.Id &&
                                     (record.ClaimOwner == null || record.ClaimExpiresAt <= now) &&
                                     (record.NextAttemptAt == null || record.NextAttemptAt <= now) &&
                                     record.Status != ImageDistributionStatus.Ready)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(record => record.Status, status)
                        .SetProperty(record => record.ClaimOwner, _claimOwner)
                        .SetProperty(record => record.ClaimExpiresAt, now.Add(ClaimDuration))
                        .SetProperty(record => record.AttemptCount, record => record.AttemptCount + 1)
                        .SetProperty(record => record.ProgressUpdatedAt, now), token);
                if (affected == 1)
                    claimed.Add(candidate);
                continue;
            }

            var record = await context.ImageDistributionRecords.SingleOrDefaultAsync(
                item => item.Id == candidate.Id, token);
            if (record is null || record.Status == ImageDistributionStatus.Ready ||
                record.ClaimOwner is not null && record.ClaimExpiresAt > now)
                continue;
            record.Status = status;
            record.ClaimOwner = _claimOwner;
            record.ClaimExpiresAt = now.Add(ClaimDuration);
            record.AttemptCount++;
            record.ProgressUpdatedAt = now;
            await context.SaveChangesAsync(token);
            claimed.Add(candidate);
        }

        return claimed;
    }

    async Task ProcessOneAsync(ClaimedImageWork work, CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var manifestJson = await context.WorkerNodes.AsNoTracking()
                .Where(node => node.Id == work.WorkerNodeId)
                .Select(node => node.CapabilityManifestJson)
                .SingleOrDefaultAsync(token);
            var limits = AgentCapabilityEvaluator.Parse(manifestJson)?.ExecutionLimits;
            var category = work.Operation == ImageDistributionOperation.Cleanup
                ? NodeDispatchCategory.Control
                : work.ImageType == ImageType.Docker
                    ? NodeDispatchCategory.DockerImageTransfer
                    : NodeDispatchCategory.VmImageTransfer;
            var limit = category switch
            {
                NodeDispatchCategory.DockerImageTransfer => limits?.DockerImageTransfers ?? 2,
                NodeDispatchCategory.VmImageTransfer => limits?.VmImageTransfers ?? 1,
                _ => limits?.ControlOperations ?? 2
            };
            var service = scope.ServiceProvider.GetRequiredService<ImageDistributionService>();
            await dispatchLimiter.RunAsync(work.WorkerNodeId, category, Math.Max(1, limit),
                operationToken => service.ProcessClaimedAsync(work.Id, _claimOwner, operationToken), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image distribution worker failed to process record {RecordId}.", work.Id);
        }
    }

    sealed record ClaimedImageWork(
        Guid Id,
        Guid WorkerNodeId,
        ImageType ImageType,
        ImageDistributionOperation Operation);
}
