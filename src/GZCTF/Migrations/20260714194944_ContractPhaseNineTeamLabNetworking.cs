using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class ContractPhaseNineTeamLabNetworking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimes" runtime
                        WHERE runtime."Status" IN (3, 4, 5)
                          AND NOT EXISTS (
                              SELECT 1 FROM "TeamLabRuntimeShards" shard
                              WHERE shard."RuntimeId" = runtime."Id"
                                AND shard."Generation" = runtime."Generation")) THEN
                        RAISE EXCEPTION 'Phase 9 contract blocked: an active runtime has no current-generation shard facts.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimeNetworks" network
                        JOIN "TeamLabRuntimes" runtime ON runtime."Id" = network."RuntimeId"
                        WHERE runtime."Status" IN (3, 4, 5)
                          AND network."Generation" = runtime."Generation"
                          AND (network."ShardId" IS NULL OR network."WorkerNodeId" IS NULL)) THEN
                        RAISE EXCEPTION 'Phase 9 contract blocked: an active runtime network has incomplete placement facts.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimeAssets" asset
                        JOIN "TeamLabRuntimes" runtime ON runtime."Id" = asset."RuntimeId"
                        WHERE runtime."Status" IN (3, 4, 5)
                          AND asset."Generation" = runtime."Generation"
                          AND (asset."ShardId" IS NULL OR asset."WorkerNodeId" IS NULL)) THEN
                        RAISE EXCEPTION 'Phase 9 contract blocked: an active runtime asset has incomplete placement facts.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimes" runtime
                        WHERE runtime."Status" IN (3, 4, 5)
                          AND EXISTS (
                              SELECT 1 FROM "TeamLabRuntimeNetworks" network
                              WHERE network."RuntimeId" = runtime."Id"
                                AND network."Generation" = runtime."Generation")
                          AND NOT EXISTS (
                              SELECT 1 FROM "TeamLabRuntimeInfrastructure" infrastructure
                              WHERE infrastructure."RuntimeId" = runtime."Id"
                                AND infrastructure."Generation" = runtime."Generation")) THEN
                        RAISE EXCEPTION 'Phase 9 contract blocked: an active runtime has no current-generation infrastructure facts.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimeInfrastructure" infrastructure
                        JOIN "TeamLabRuntimes" runtime ON runtime."Id" = infrastructure."RuntimeId"
                        WHERE runtime."Status" IN (3, 4, 5)
                          AND infrastructure."Generation" = runtime."Generation"
                          AND NOT EXISTS (
                              SELECT 1 FROM "TeamLabRuntimeInfrastructureFragments" fragment
                              WHERE fragment."InfrastructureId" = infrastructure."Id")) THEN
                        RAISE EXCEPTION 'Phase 9 contract blocked: an active runtime infrastructure fact has no fragment.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimeShards" shard
                        JOIN "TeamLabRuntimes" runtime ON runtime."Id" = shard."RuntimeId"
                        WHERE runtime."Status" IN (3, 4, 5)
                          AND shard."Generation" = runtime."Generation"
                          AND NOT EXISTS (
                              SELECT 1 FROM "TeamLabFabricLinkLeases" lease
                              WHERE lease."RuntimeId" = shard."RuntimeId"
                                AND lease."Generation" = shard."Generation"
                                AND lease."ShardId" = shard."Id"
                                AND lease."ReleasedAt" IS NULL)) THEN
                        RAISE EXCEPTION 'Phase 9 contract blocked: an active runtime shard has no Fabric link lease.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabTrafficCaptureJobs" job
                        WHERE job."FilePath" IS NOT NULL
                          AND btrim(job."FilePath") <> '') THEN
                        RAISE EXCEPTION 'Phase 9 contract blocked: legacy capture files must be imported into object storage or explicitly expired before contraction.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_TeamLabRuntimeNetworks_NetworkId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_TeamLabRuntimeShards_ShardId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_WorkerNodes_WorkerNodeId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTrafficCaptureJobs_NetworkId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTrafficCaptureJobs_ShardId_Status",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTrafficCaptureJobs_WorkerNodeId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "NetworkId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "ShardId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "WorkerNodeId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.AddColumn<string>(
                name: "LastSensorErrorCode",
                table: "TeamLabObservationCursors",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SensorRejectedCount",
                table: "TeamLabObservationCursors",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSensorErrorCode",
                table: "TeamLabObservationCursors");

            migrationBuilder.DropColumn(
                name: "SensorRejectedCount",
                table: "TeamLabObservationCursors");

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NetworkId",
                table: "TeamLabTrafficCaptureJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShardId",
                table: "TeamLabTrafficCaptureJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkerNodeId",
                table: "TeamLabTrafficCaptureJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_NetworkId",
                table: "TeamLabTrafficCaptureJobs",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_ShardId_Status",
                table: "TeamLabTrafficCaptureJobs",
                columns: new[] { "ShardId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_WorkerNodeId",
                table: "TeamLabTrafficCaptureJobs",
                column: "WorkerNodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_TeamLabRuntimeNetworks_NetworkId",
                table: "TeamLabTrafficCaptureJobs",
                column: "NetworkId",
                principalTable: "TeamLabRuntimeNetworks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_TeamLabRuntimeShards_ShardId",
                table: "TeamLabTrafficCaptureJobs",
                column: "ShardId",
                principalTable: "TeamLabRuntimeShards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_WorkerNodes_WorkerNodeId",
                table: "TeamLabTrafficCaptureJobs",
                column: "WorkerNodeId",
                principalTable: "WorkerNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
