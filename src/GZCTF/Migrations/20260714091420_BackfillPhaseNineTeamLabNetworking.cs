using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260714091420_BackfillPhaseNineTeamLabNetworking")]
public partial class BackfillPhaseNineTeamLabNetworking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "TeamLabTopologyConnections"
            SET "Direction" = 1;

            WITH current_networks AS (
                SELECT n.*, r."CreatedAt" AS runtime_created_at
                FROM "TeamLabRuntimeNetworks" n
                JOIN "TeamLabRuntimes" r ON r."Id" = n."RuntimeId"
                JOIN "TeamLabTopologyReleases" rel ON rel."Id" = r."TopologyReleaseId"
                WHERE n."Generation" = r."Generation"
                  AND rel."SchemaVersion" = 1
                  AND r."Status" <> 10
            )
            INSERT INTO "TeamLabRuntimeInfrastructure"
                ("PublicId", "RuntimeId", "Generation", "TopologyKey", "Name", "Kind",
                 "NetworkKey", "InterfaceSummaryJson", "ConnectionSummaryJson", "Status",
                 "DesiredStateDigest", "LastError", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), n."RuntimeId", n."Generation", 'switch-' || n."TopologyKey",
                   n."Name" || ' switch', 0, n."TopologyKey", '[]'::jsonb, '[]'::jsonb,
                   0, NULL, NULL, n.runtime_created_at, NULL
            FROM current_networks n
            ON CONFLICT ("RuntimeId", "Generation", "TopologyKey") DO NOTHING;

            WITH current_networks AS (
                SELECT n.*
                FROM "TeamLabRuntimeNetworks" n
                JOIN "TeamLabRuntimes" r ON r."Id" = n."RuntimeId"
                JOIN "TeamLabTopologyReleases" rel ON rel."Id" = r."TopologyReleaseId"
                WHERE n."Generation" = r."Generation"
                  AND rel."SchemaVersion" = 1
                  AND r."Status" <> 10
            )
            INSERT INTO "TeamLabRuntimeInfrastructureFragments"
                ("PublicId", "InfrastructureId", "ShardId", "WorkerNodeId", "FragmentKey",
                 "InterfaceSummaryJson", "Status", "NativeResourceId", "DesiredStateDigest",
                 "LastError", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), i."Id", n."ShardId", n."WorkerNodeId",
                   'switch-' || n."TopologyKey" || '-shard-' || n."ShardId",
                   '[]'::jsonb, 0, n."BridgeName", NULL, NULL, now(), NULL
            FROM current_networks n
            JOIN "TeamLabRuntimeInfrastructure" i
              ON i."RuntimeId" = n."RuntimeId"
             AND i."Generation" = n."Generation"
             AND i."TopologyKey" = 'switch-' || n."TopologyKey"
            ON CONFLICT ("InfrastructureId", "ShardId") DO NOTHING;

            WITH candidates AS (
                SELECT s.*
                FROM "TeamLabRuntimeShards" s
                JOIN "TeamLabRuntimes" r ON r."Id" = s."RuntimeId"
                JOIN "TeamLabTopologyReleases" rel ON rel."Id" = r."TopologyReleaseId"
                WHERE s."Generation" = r."Generation"
                  AND rel."SchemaVersion" = 1
                  AND r."Status" <> 10
                  AND NOT EXISTS (
                      SELECT 1 FROM "TeamLabFabricLinkLeases" lease
                      WHERE lease."RuntimeId" = s."RuntimeId"
                        AND lease."Generation" = s."Generation"
                        AND lease."ShardId" = s."Id"
                        AND lease."ReleasedAt" IS NULL)
            ), ranked AS (
                SELECT candidates.*, row_number() OVER (
                    ORDER BY "RuntimeId", "Generation", "WorkerNodeId", "Id") AS allocation_ordinal
                FROM candidates
            ), available AS (
                SELECT slot, row_number() OVER (ORDER BY slot) AS allocation_ordinal
                FROM generate_series(0, 16383) AS generated(slot)
                WHERE NOT EXISTS (
                    SELECT 1 FROM "TeamLabFabricLinkLeases" lease
                    WHERE lease."ReleasedAt" IS NULL
                      AND lease."AllocatedCidr" &&
                          format('169.254.%s.%s/30', slot / 64, mod(slot, 64) * 4)::cidr)
            )
            INSERT INTO "TeamLabFabricLinkLeases"
                ("RuntimeId", "Generation", "ShardId", "WorkerNodeId", "AllocatedCidr",
                 "HubAddress", "NodeAddress", "AllocatedAt", "ReleasedAt")
            SELECT ranked."RuntimeId", ranked."Generation", ranked."Id", ranked."WorkerNodeId",
                   format('169.254.%s.%s/30', available.slot / 64, mod(available.slot, 64) * 4)::cidr,
                   format('169.254.%s.%s', available.slot / 64, mod(available.slot, 64) * 4 + 1),
                   format('169.254.%s.%s', available.slot / 64, mod(available.slot, 64) * 4 + 2),
                   ranked."CreatedAt", NULL
            FROM ranked
            JOIN available USING (allocation_ordinal)
            ON CONFLICT ("RuntimeId", "Generation", "ShardId") DO NOTHING;

            WITH current_networks AS (
                SELECT n.*
                FROM "TeamLabRuntimeNetworks" n
                JOIN "TeamLabRuntimes" r ON r."Id" = n."RuntimeId"
                JOIN "TeamLabTopologyReleases" rel ON rel."Id" = r."TopologyReleaseId"
                WHERE n."Generation" = r."Generation"
                  AND rel."SchemaVersion" = 1
                  AND r."Status" <> 10
            )
            INSERT INTO "TeamLabObservationPoints"
                ("PublicId", "RuntimeId", "Generation", "WorkerNodeId", "ShardId", "NetworkId",
                 "InfrastructureFragmentId", "AssetId", "Kind", "TopologyKey", "InterfaceToken",
                 "DesiredStateDigest", "Enabled", "LastSequence", "DroppedPackets", "LastError",
                 "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), n."RuntimeId", n."Generation", n."WorkerNodeId", n."ShardId", n."Id",
                   f."Id", NULL, 0, n."TopologyKey", n."BridgeName", NULL, true, 0, 0, NULL, now(), NULL
            FROM current_networks n
            LEFT JOIN "TeamLabRuntimeInfrastructure" i
              ON i."RuntimeId" = n."RuntimeId"
             AND i."Generation" = n."Generation"
             AND i."TopologyKey" = 'switch-' || n."TopologyKey"
            LEFT JOIN "TeamLabRuntimeInfrastructureFragments" f
              ON f."InfrastructureId" = i."Id" AND f."ShardId" = n."ShardId"
            ON CONFLICT ("RuntimeId", "Generation", "WorkerNodeId", "InterfaceToken", "Kind") DO NOTHING;

            WITH current_shards AS (
                SELECT s.*
                FROM "TeamLabRuntimeShards" s
                JOIN "TeamLabRuntimes" r ON r."Id" = s."RuntimeId"
                JOIN "TeamLabTopologyReleases" rel ON rel."Id" = r."TopologyReleaseId"
                WHERE s."Generation" = r."Generation"
                  AND rel."SchemaVersion" = 1
                  AND r."Status" <> 10
            )
            INSERT INTO "TeamLabObservationPoints"
                ("PublicId", "RuntimeId", "Generation", "WorkerNodeId", "ShardId", "NetworkId",
                 "InfrastructureFragmentId", "AssetId", "Kind", "TopologyKey", "InterfaceToken",
                 "DesiredStateDigest", "Enabled", "LastSequence", "DroppedPackets", "LastError",
                 "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), s."RuntimeId", s."Generation", s."WorkerNodeId", s."Id", NULL,
                   NULL, NULL, 2, 'fabric-' || s."Id", 'fabric-shard-' || s."Id",
                   NULL, true, 0, 0, NULL, now(), NULL
            FROM current_shards s
            ON CONFLICT ("RuntimeId", "Generation", "WorkerNodeId", "InterfaceToken", "Kind") DO NOTHING;

            UPDATE "TeamLabTrafficCaptureJobs" job
            SET "NetworkKey" = network."TopologyKey"
            FROM "TeamLabRuntimeNetworks" network
            WHERE job."NetworkId" = network."Id"
              AND job."NetworkKey" IS NULL;

            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Runtime facts may have become live after backfill; contraction deliberately preserves them.
    }
}
