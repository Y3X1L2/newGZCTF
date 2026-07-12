using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class OperationalAggregationService(AppDbContext context)
{
    public Task<int> AggregateSystemLogsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "OperationalLogAggregates"
                ("BucketStart", "Level", "Logger", "Count", "UpdatedAt")
            SELECT {{start}}, "Level", "Logger", COUNT(*), CURRENT_TIMESTAMP
            FROM "Logs"
            WHERE "TimeUtc" >= {{start}} AND "TimeUtc" < {{end}}
            GROUP BY "Level", "Logger"
            ON CONFLICT ("BucketStart", "Level", "Logger")
            DO UPDATE SET "Count" = EXCLUDED."Count", "UpdatedAt" = CURRENT_TIMESTAMP
            """, cancellationToken);

    public Task<int> AggregateTeamLabFlowsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "TeamLabTrafficFlowAggregates"
                ("BucketStart", "RuntimeId", "Generation", "ShardId", "NetworkId", "Protocol",
                 "SourcePrefix", "DestinationPrefix", "FlowCount", "PacketCount", "Bytes", "UpdatedAt")
            SELECT {{start}}, "RuntimeId", "Generation", COALESCE("ShardId", 0), COALESCE("NetworkId", 0), "Protocol",
                   "SourcePrefix", "DestinationPrefix", COUNT(*), SUM("Packets"), SUM("Bytes"), CURRENT_TIMESTAMP
            FROM "TeamLabTrafficFlows"
            WHERE "CapturedAt" >= {{start}} AND "CapturedAt" < {{end}}
            GROUP BY "RuntimeId", "Generation", "ShardId", "NetworkId", "Protocol",
                     "SourcePrefix", "DestinationPrefix"
            ON CONFLICT ("BucketStart", "RuntimeId", "Generation", "ShardId", "NetworkId", "Protocol",
                         "SourcePrefix", "DestinationPrefix")
            DO UPDATE SET "FlowCount" = EXCLUDED."FlowCount",
                          "PacketCount" = EXCLUDED."PacketCount",
                          "Bytes" = EXCLUDED."Bytes",
                          "UpdatedAt" = CURRENT_TIMESTAMP
            """, cancellationToken);

    public Task<int> AggregateDeploymentLifecycleAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "DeploymentLifecycleAggregates"
                ("BucketStart", "Kind", "Status", "WorkerNodeId", "Count", "DurationCount",
                 "DurationTotalMilliseconds", "DurationMaxMilliseconds", "UpdatedAt")
            SELECT {{start}}, "Kind", "Status", COALESCE("TargetNodeId", '00000000-0000-0000-0000-000000000000'::uuid), COUNT(*),
                   COUNT(*) FILTER (WHERE "StartedAt" IS NOT NULL AND "CompletedAt" IS NOT NULL),
                   COALESCE(SUM(EXTRACT(EPOCH FROM ("CompletedAt" - "StartedAt")) * 1000)
                       FILTER (WHERE "StartedAt" IS NOT NULL AND "CompletedAt" IS NOT NULL), 0)::bigint,
                   COALESCE(MAX(EXTRACT(EPOCH FROM ("CompletedAt" - "StartedAt")) * 1000)
                       FILTER (WHERE "StartedAt" IS NOT NULL AND "CompletedAt" IS NOT NULL), 0)::bigint,
                   CURRENT_TIMESTAMP
            FROM "DeploymentQueueTickets"
            WHERE "CompletedAt" >= {{start}} AND "CompletedAt" < {{end}} AND "Status" IN (3, 4, 5)
            GROUP BY "Kind", "Status", "TargetNodeId"
            ON CONFLICT ("BucketStart", "Kind", "Status", "WorkerNodeId")
            DO UPDATE SET "Count" = EXCLUDED."Count",
                          "DurationCount" = EXCLUDED."DurationCount",
                          "DurationTotalMilliseconds" = EXCLUDED."DurationTotalMilliseconds",
                          "DurationMaxMilliseconds" = EXCLUDED."DurationMaxMilliseconds",
                          "UpdatedAt" = CURRENT_TIMESTAMP
            """, cancellationToken);
}
