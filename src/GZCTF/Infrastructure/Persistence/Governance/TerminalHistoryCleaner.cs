using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class TerminalHistoryCleaner(AppDbContext context)
{
    public async Task<int> CleanExpiredTeamLabCaptureArtifactsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var segments = await context.TeamLabTrafficCaptureSegments
            .Include(item => item.CaptureJob)
            .Where(item => item.CaptureJob.ExpiresAt <= now &&
                           item.Status != TeamLabTrafficCaptureSegmentStatus.Expired)
            .OrderBy(item => item.CaptureJob.ExpiresAt)
            .ThenBy(item => item.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        foreach (var segment in segments)
        {
            segment.Status = TeamLabTrafficCaptureSegmentStatus.CleanupPending;
            segment.LastError = "Capture artifact cleanup is pending and will be coordinated with the WorkerNode.";
            segment.UpdatedAt = now;
        }
        if (segments.Length == 0) return 0;
        var jobIds = segments.Select(item => item.CaptureJobId).Distinct().ToArray();
        var jobs = await context.TeamLabTrafficCaptureJobs
            .Include(item => item.Segments)
            .Where(item => jobIds.Contains(item.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.Status = TeamLabTrafficCaptureStatus.CleanupPending;
            job.LastError = "Capture artifact cleanup is pending and will be retried.";
        }
        await context.SaveChangesAsync(cancellationToken);
        return segments.Length;
    }

    public Task<int> CleanOperationalEventsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "Id" FROM "OperationalEvents"
            WHERE "OccurredAt" < {{cutoff}}
            ORDER BY "OccurredAt", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "OperationalEvents" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanWorkerNodeMetricsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "WorkerNodeId", "WindowStart" FROM "WorkerNodeMetricSamples"
            WHERE "WindowStart" < {{cutoff}}
            ORDER BY "WindowStart", "WorkerNodeId" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "WorkerNodeMetricSamples" target USING candidates
        WHERE target."WorkerNodeId" = candidates."WorkerNodeId"
          AND target."WindowStart" = candidates."WindowStart"
        """, cancellationToken);

    public Task<int> CleanDeploymentTicketsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT ticket."Id" FROM "DeploymentQueueTickets" ticket
            WHERE ticket."Status" IN (3, 4, 5) AND ticket."CompletedAt" < {{cutoff}}
              AND NOT EXISTS (
                  SELECT 1 FROM "ApiOperations" operation
                  WHERE operation."DeploymentQueueTicketId" = ticket."Id"
                    AND operation."Status" IN (0, 1)
              )
            ORDER BY ticket."CompletedAt", ticket."Id" LIMIT {{batchSize}}
            FOR UPDATE OF ticket SKIP LOCKED
        )
        DELETE FROM "DeploymentQueueTickets" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanApiOperationsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT operation."Id" FROM "ApiOperations" operation
            WHERE operation."Status" IN (2, 3) AND operation."CompletedAt" < {{cutoff}}
              AND NOT (
                  operation."Kind" = 'asset.upload' AND operation."Status" = 2
                  AND operation."ResourceType" = 'asset'
                  AND EXISTS (SELECT 1 FROM "Files" file WHERE file."Hash" = operation."ResourceId")
              )
            ORDER BY operation."CompletedAt", operation."Id" LIMIT {{batchSize}} FOR UPDATE OF operation SKIP LOCKED
        )
        DELETE FROM "ApiOperations" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanTeamLabEventsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT event."Id"
            FROM "TeamLabEvents" event
            JOIN "TeamLabRuntimes" runtime ON runtime."Id" = event."RuntimeId"
            WHERE event."CreatedAt" < {{cutoff}} AND runtime."Status" IN (6, 8, 10)
              AND event."Generation" < runtime."Generation"
            ORDER BY event."CreatedAt", event."Id" LIMIT {{batchSize}} FOR UPDATE OF event SKIP LOCKED
        )
        DELETE FROM "TeamLabEvents" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanGovernanceRunsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "Id" FROM "DataGovernanceRuns"
            WHERE "Status" IN (1, 2, 3) AND "CompletedAt" < {{cutoff}}
            ORDER BY "CompletedAt", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "DataGovernanceRuns" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanFlowAggregatesAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "Id" FROM "TeamLabTrafficFlowAggregates"
            WHERE "BucketStart" < {{cutoff}}
            ORDER BY "BucketStart", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "TeamLabTrafficFlowAggregates" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanTeamLabObservationsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "Id" FROM "TeamLabTrafficObservations"
            WHERE "ObservedAt" < {{cutoff}}
            ORDER BY "ObservedAt", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "TeamLabTrafficObservations" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanTeamLabTrafficPathsAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "Id" FROM "TeamLabTrafficPaths"
            WHERE "EndedAt" < {{cutoff}}
            ORDER BY "EndedAt", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "TeamLabTrafficPaths" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanLogAggregatesAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "Id" FROM "OperationalLogAggregates"
            WHERE "BucketStart" < {{cutoff}}
            ORDER BY "BucketStart", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "OperationalLogAggregates" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);

    public Task<int> CleanDeploymentAggregatesAsync(DateTimeOffset cutoff, int batchSize,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync($$"""
        WITH candidates AS (
            SELECT "Id" FROM "DeploymentLifecycleAggregates"
            WHERE "BucketStart" < {{cutoff}}
            ORDER BY "BucketStart", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
        )
        DELETE FROM "DeploymentLifecycleAggregates" target USING candidates
        WHERE target."Id" = candidates."Id"
        """, cancellationToken);
}
