using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class CompletePhaseFiveRedisGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LiveMetricObservedAt",
                table: "WorkerNodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LiveMetricReceivedAt",
                table: "WorkerNodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LiveMetricSequence",
                table: "WorkerNodes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicPortLeaseId",
                table: "Containers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectionRevisions",
                columns: table => new
                {
                    Projection = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionRevisions", x => new { x.Projection, x.ResourceKey });
                });

            migrationBuilder.CreateTable(
                name: "WorkerNodeMetricSamples",
                columns: table => new
                {
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    AverageCpuLoad = table.Column<float>(type: "real", nullable: false),
                    MinimumCpuLoad = table.Column<float>(type: "real", nullable: false),
                    MaximumCpuLoad = table.Column<float>(type: "real", nullable: false),
                    AverageMemoryLoad = table.Column<float>(type: "real", nullable: false),
                    MinimumMemoryLoad = table.Column<float>(type: "real", nullable: false),
                    MaximumMemoryLoad = table.Column<float>(type: "real", nullable: false),
                    AverageContainers = table.Column<double>(type: "double precision", nullable: false),
                    MaximumContainers = table.Column<int>(type: "integer", nullable: false),
                    AverageVms = table.Column<double>(type: "double precision", nullable: false),
                    MaximumVms = table.Column<int>(type: "integer", nullable: false),
                    AverageUsedPorts = table.Column<double>(type: "double precision", nullable: false),
                    MaximumUsedPorts = table.Column<int>(type: "integer", nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    FirstReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerNodeMetricSamples", x => new { x.WorkerNodeId, x.WindowStart });
                    table.ForeignKey(
                        name: "FK_WorkerNodeMetricSamples_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionRevisions_UpdatedAt",
                table: "ProjectionRevisions",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerNodeMetricSamples_Window_Node",
                table: "WorkerNodeMetricSamples",
                columns: new[] { "WindowStart", "WorkerNodeId" });

            migrationBuilder.Sql("""
                UPDATE "Containers"
                SET "PublicPortLeaseId" = gen_random_uuid()
                WHERE "PublicPort" IS NOT NULL AND "PublicPortLeaseId" IS NULL;

                UPDATE "WorkerNodes"
                SET "LiveMetricObservedAt" = "LastHeartbeat",
                    "LiveMetricReceivedAt" = "LastHeartbeat",
                    "LiveMetricSequence" = GREATEST(
                        "LiveMetricSequence",
                        (EXTRACT(EPOCH FROM "LastHeartbeat") * 1000)::bigint)
                WHERE "LastHeartbeat" IS NOT NULL;

                INSERT INTO "ProjectionRevisions" ("Projection", "ResourceKey", "Version", "UpdatedAt")
                SELECT 'scoreboard', "Id"::text, 1, CURRENT_TIMESTAMP
                FROM "Games"
                ON CONFLICT ("Projection", "ResourceKey") DO NOTHING;

                INSERT INTO "ProjectionRevisions" ("Projection", "ResourceKey", "Version", "UpdatedAt")
                SELECT DISTINCT 'theory-statistics', "GameId"::text, 1, CURRENT_TIMESTAMP
                FROM "TheoryPapers"
                WHERE "GameId" > 0
                ON CONFLICT ("Projection", "ResourceKey") DO NOTHING;

                INSERT INTO "ProjectionRevisions" ("Projection", "ResourceKey", "Version", "UpdatedAt")
                SELECT 'training-statistics', '__global__', 1, CURRENT_TIMESTAMP
                WHERE EXISTS (SELECT 1 FROM "TrainingCourses")
                ON CONFLICT ("Projection", "ResourceKey") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectionRevisions");

            migrationBuilder.DropTable(
                name: "WorkerNodeMetricSamples");

            migrationBuilder.DropColumn(
                name: "LiveMetricObservedAt",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "LiveMetricReceivedAt",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "LiveMetricSequence",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "PublicPortLeaseId",
                table: "Containers");
        }
    }
}
