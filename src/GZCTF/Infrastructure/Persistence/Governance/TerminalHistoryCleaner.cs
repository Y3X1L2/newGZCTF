using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class TerminalHistoryCleaner(AppDbContext context)
{
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
            SELECT "Id" FROM "ApiOperations"
            WHERE "Status" IN (2, 3) AND "CompletedAt" < {{cutoff}}
            ORDER BY "CompletedAt", "Id" LIMIT {{batchSize}} FOR UPDATE SKIP LOCKED
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
