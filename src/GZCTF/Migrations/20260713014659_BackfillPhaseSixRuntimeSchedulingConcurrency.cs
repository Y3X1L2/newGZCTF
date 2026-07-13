using Microsoft.EntityFrameworkCore.Migrations;

using System;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class BackfillPhaseSixRuntimeSchedulingConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "WorkerNodes"
                SET "AgentVersion" = NULLIF("TeamLabAgentVersion", ''),
                    "CapabilityManifestSchemaVersion" = 0,
                    "CapabilityManifestJson" = '{}',
                    "CapabilityHash" = NULL,
                    "CapabilityObservedAt" = NULL;

                UPDATE "TeamLabRuntimeNetworks"
                SET "PlacementGroupKey" = "TopologyKey";

                UPDATE "TeamLabRuntimeAssets"
                SET "PlacementGroupKey" = COALESCE(NULLIF("NetworkKey", ''), "TopologyKey");

                WITH entry_network AS (
                    SELECT network."Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY network."RuntimeId", network."Generation"
                               ORDER BY network."Id") AS ordinal
                    FROM "TeamLabRuntimeNetworks" AS network
                    JOIN "TeamLabRuntimes" AS runtime
                      ON runtime."Id" = network."RuntimeId"
                     AND runtime."Generation" = network."Generation"
                     AND runtime."EntryShardId" = network."ShardId"
                )
                UPDATE "TeamLabRuntimeNetworks" AS network
                SET "IsEntry" = true
                FROM entry_network
                WHERE network."Id" = entry_network."Id" AND entry_network.ordinal = 1;

                UPDATE "ImageDistributionRecords"
                SET "Operation" = CASE WHEN "Status" = 4 THEN 1 ELSE 0 END,
                    "Stage" = CASE
                        WHEN "Status" IN (0, 4) THEN 1
                        WHEN "Status" = 1 THEN 3
                        ELSE 0
                    END,
                    "ProgressUpdatedAt" = COALESCE("LastCheckedAt", "CreatedAt"),
                    "NextAttemptAt" = CASE WHEN "Status" IN (0, 3, 4) THEN CURRENT_TIMESTAMP ELSE NULL END,
                    "LastErrorCode" = CASE WHEN "Status" = 3 THEN 'legacy_distribution_failed' ELSE NULL END;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "DeploymentTargets" AS target
                        LEFT JOIN "DeploymentQueueTickets" AS ticket
                          ON ticket."DeploymentTargetId" = target."Id"
                        WHERE ticket."Id" IS NULL AND target."Status" IN (0, 1, 5, 6)
                    ) THEN
                        RAISE EXCEPTION 'Phase 6 backfill aborted: active orphan DeploymentTargets require manual resolution';
                    END IF;
                END $$;

                UPDATE "DeploymentQueueTickets" AS ticket
                SET "TargetNodeId" = COALESCE(ticket."TargetNodeId", target."TargetNodeId"),
                    "ErrorMessage" = COALESCE(ticket."ErrorMessage", target."ErrorMessage"),
                    "CompletedAt" = COALESCE(ticket."CompletedAt", target."CompletedAt")
                FROM "DeploymentTargets" AS target
                WHERE ticket."DeploymentTargetId" = target."Id";

                UPDATE "DeploymentQueueTickets"
                SET "Kind" = CASE "Kind" WHEN 3 THEN 6 WHEN 4 THEN 7 ELSE "Kind" END,
                    "Status" = CASE "Status"
                        WHEN 0 THEN 0
                        WHEN 1 THEN 2
                        WHEN 2 THEN 3
                        WHEN 3 THEN 4
                        WHEN 4 THEN 5
                        WHEN 5 THEN 6
                    END,
                    "Operation" = 1,
                    "Generation" = 1,
                    "Stage" = CASE "Status"
                        WHEN 0 THEN 0
                        WHEN 1 THEN 6
                        WHEN 2 THEN CASE WHEN "Kind" = 3 THEN 8 ELSE 7 END
                        WHEN 3 THEN 17
                        WHEN 4 THEN 18
                        WHEN 5 THEN 19
                    END;

                UPDATE "DeploymentQueueTickets"
                SET "SubjectConcurrencyKey" = CASE "Kind"
                    WHEN 1 THEN 'game-container:' || "GameId" || ':' || "OwnerTeamId" || ':' || "ChallengeId"
                    WHEN 2 THEN 'exercise-container:' || "OwnerUserId" || ':' || "ChallengeId"
                    WHEN 6 THEN 'vm:' || "GameId" || ':' || "OwnerUserId" || ':' || "ChallengeId" || ':' || "VmInstanceId"
                    WHEN 7 THEN 'teamlab-runtime:' || "TeamLabRuntimeId"
                    ELSE 'legacy-ticket:' || "Id"
                END;

                UPDATE "DeploymentQueueTickets"
                SET "ActiveIdentity" = 'Create:' || "SubjectConcurrencyKey" || ':1';

                INSERT INTO "DeploymentQueueTickets" (
                    "Id", "Kind", "Operation", "Status", "Stage", "TargetNodeId",
                    "DockerSlots", "VmSlots", "Generation", "ActiveIdentity",
                    "SubjectConcurrencyKey", "SubjectType", "SubjectPublicId",
                    "SubjectDisplayName", "ResourceDisplayName", "ErrorMessage",
                    "AttemptCount", "CreatedAt", "CompletedAt")
                SELECT target."Id",
                       CASE WHEN target."Type" = 1 THEN 6 ELSE 5 END,
                       CASE target."Action" WHEN 2 THEN 5 WHEN 3 THEN 4 ELSE 1 END,
                       CASE target."Status" WHEN 2 THEN 4 WHEN 3 THEN 5 WHEN 4 THEN 6 END,
                       CASE target."Status" WHEN 2 THEN 17 WHEN 3 THEN 18 WHEN 4 THEN 19 END,
                       target."TargetNodeId",
                       CASE WHEN target."Type" = 0 THEN 1 ELSE 0 END,
                       CASE WHEN target."Type" = 1 THEN 1 ELSE 0 END,
                       1,
                       'legacy-target:' || target."Id",
                       'legacy-target:' || target."Id",
                       'system-maintenance',
                       target."Id"::text,
                       'Legacy deployment target',
                       CASE WHEN target."Type" = 1 THEN 'VM' ELSE 'Docker' END,
                       target."ErrorMessage",
                       0,
                       target."CreatedAt",
                       target."CompletedAt"
                FROM "DeploymentTargets" AS target
                LEFT JOIN "DeploymentQueueTickets" AS ticket
                  ON ticket."DeploymentTargetId" = target."Id"
                WHERE ticket."Id" IS NULL AND target."Status" IN (2, 3, 4);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Phase 6 backfill is intentionally irreversible; restore the pre-migration database backup instead.");
        }
    }
}
