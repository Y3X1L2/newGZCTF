using System.Diagnostics;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
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
        await ReclaimLocalOrphanedClaimsAsync(stoppingToken);
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

    async Task ReclaimLocalOrphanedClaimsAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var localOwnerPrefix = $"image:{Environment.MachineName}:";
        var now = DateTimeOffset.UtcNow;
        var reclaimed = await context.ImageDistributionRecords
            .Where(record => record.ClaimOwner != null &&
                             record.ClaimOwner.StartsWith(localOwnerPrefix) &&
                             record.ClaimOwner != _claimOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.Status, record =>
                    record.Operation == ImageDistributionOperation.Cleanup
                        ? ImageDistributionStatus.CleanupPending
                        : ImageDistributionStatus.Pending)
                .SetProperty(record => record.ClaimOwner, (string?)null)
                .SetProperty(record => record.ClaimExpiresAt, (DateTimeOffset?)null)
                .SetProperty(record => record.NextAttemptAt, now)
                .SetProperty(record => record.ProgressUpdatedAt, now), token);
        if (reclaimed > 0)
            logger.LogWarning(
                "Reclaimed {Count} image distribution claim(s) left by an earlier process on {MachineName}.",
                reclaimed, Environment.MachineName);
    }

    async Task<IReadOnlyList<ClaimedImageWork>> ClaimBatchAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<IOperationalEventWriter>();
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
                record.Operation, record.Status))
            .ToArrayAsync(token);

        List<ClaimedImageWork> claimed = [];
        foreach (var candidate in candidates)
        {
            var status = candidate.Operation == ImageDistributionOperation.Cleanup
                ? ImageDistributionStatus.CleanupPending
                : ImageDistributionStatus.Pulling;
            if (context.Database.IsRelational())
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);
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
                {
                    var claimedRecord = await context.ImageDistributionRecords
                        .Include(item => item.ImageTemplate)
                        .SingleAsync(item => item.Id == candidate.Id, token);
                    AppendClaimEvents(events, claimedRecord, candidate.Status);
                    await context.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                    claimed.Add(candidate);
                }
                else
                {
                    await transaction.RollbackAsync(token);
                }
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
            AppendClaimEvents(events, record, candidate.Status);
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
            var correlation = scope.ServiceProvider.GetRequiredService<OperationalCorrelation>();
            var manifestJson = await context.WorkerNodes.AsNoTracking()
                .Where(node => node.Id == work.WorkerNodeId)
                .Select(node => node.CapabilityManifestJson)
                .SingleOrDefaultAsync(token);
            var limits = AgentCapabilityEvaluator.Parse(manifestJson)?.ExecutionLimits;
            var category = work.Operation == ImageDistributionOperation.Cleanup
                ? NodeDispatchCategory.Cleanup
                : work.ImageType == ImageType.Docker
                    ? NodeDispatchCategory.DockerImageTransfer
                    : NodeDispatchCategory.VmImageTransfer;
            var limit = NodeDispatchLimitPolicy.Resolve(limits, category);
            var service = scope.ServiceProvider.GetRequiredService<ImageDistributionService>();
            using var correlationScope = correlation.Begin(work.Id);
            using var activity = PlatformTelemetry.ImageActivitySource.StartActivity(
                work.Operation == ImageDistributionOperation.Cleanup
                    ? "image.cleanup"
                    : "image.distribute",
                ActivityKind.Consumer);
            activity?.SetTag("image.type", work.ImageType.ToString());
            activity?.SetTag("image.operation", work.Operation.ToString());
            activity?.SetTag("gzctf.image_distribution_id", work.Id.ToString());
            activity?.SetTag("gzctf.worker_node_id", work.WorkerNodeId.ToString());
            try
            {
                await dispatchLimiter.RunAsync(work.WorkerNodeId, category, Math.Max(1, limit),
                    operationToken => service.ProcessClaimedAsync(work.Id, _claimOwner, operationToken), token);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                throw;
            }
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
        ImageDistributionOperation Operation,
        ImageDistributionStatus Status);

    static void AppendClaimEvents(
        IOperationalEventWriter events,
        ImageDistributionRecord record,
        ImageDistributionStatus previousStatus)
    {
        record.LastCorrelationId = record.Id;
        if (previousStatus == ImageDistributionStatus.Failed)
            events.Append(ImageDistributionService.BuildOperationalEvent(
                record,
                OperationalEventCodes.Image.DistributionRetryQueued,
                OperationalEventOutcome.Pending,
                "Image distribution retry was queued.",
                OperationalEventSeverity.Warning));
        events.Append(ImageDistributionService.BuildOperationalEvent(
            record,
            OperationalEventCodes.Image.DistributionClaimed,
            OperationalEventOutcome.Started,
            "Image distribution work was claimed by a worker."));
    }
}
