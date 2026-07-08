using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabMultinodeFabricRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TeamLabAgentVersion",
                table: "WorkerNodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamLabCapabilitiesJson",
                table: "WorkerNodes",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TeamLabFabricIp",
                table: "WorkerNodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "TeamLabFabricStatus",
                table: "WorkerNodes",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "TeamLabProtocolVersion",
                table: "WorkerNodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ShardId",
                table: "TeamLabRuntimeNetworks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkerNodeId",
                table: "TeamLabRuntimeNetworks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShardId",
                table: "TeamLabRuntimeAssets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkerNodeId",
                table: "TeamLabRuntimeAssets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeShards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    RouteVersion = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeShards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeShards_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeShards_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTrafficCaptureJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    ShardId = table.Column<int>(type: "integer", nullable: true),
                    NetworkId = table.Column<int>(type: "integer", nullable: true),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MaxBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxSeconds = table.Column<int>(type: "integer", nullable: false),
                    CapturedBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTrafficCaptureJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCaptureJobs_TeamLabRuntimeNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "TeamLabRuntimeNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCaptureJobs_TeamLabRuntimeShards_ShardId",
                        column: x => x.ShardId,
                        principalTable: "TeamLabRuntimeShards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCaptureJobs_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCaptureJobs_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTrafficFlows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    ShardId = table.Column<int>(type: "integer", nullable: true),
                    NetworkId = table.Column<int>(type: "integer", nullable: true),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourcePort = table.Column<int>(type: "integer", nullable: true),
                    DestinationIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DestinationPort = table.Column<int>(type: "integer", nullable: true),
                    Protocol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTrafficFlows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficFlows_TeamLabRuntimeNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "TeamLabRuntimeNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficFlows_TeamLabRuntimeShards_ShardId",
                        column: x => x.ShardId,
                        principalTable: "TeamLabRuntimeShards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficFlows_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficFlows_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeNetworks_ShardId",
                table: "TeamLabRuntimeNetworks",
                column: "ShardId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeNetworks_WorkerNodeId",
                table: "TeamLabRuntimeNetworks",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_ShardId",
                table: "TeamLabRuntimeAssets",
                column: "ShardId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_WorkerNodeId",
                table: "TeamLabRuntimeAssets",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeShards_RuntimeId_WorkerNodeId",
                table: "TeamLabRuntimeShards",
                columns: new[] { "RuntimeId", "WorkerNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeShards_WorkerNodeId",
                table: "TeamLabRuntimeShards",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_NetworkId",
                table: "TeamLabTrafficCaptureJobs",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_RuntimeId_Status",
                table: "TeamLabTrafficCaptureJobs",
                columns: new[] { "RuntimeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_ShardId_Status",
                table: "TeamLabTrafficCaptureJobs",
                columns: new[] { "ShardId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_WorkerNodeId",
                table: "TeamLabTrafficCaptureJobs",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficFlows_NetworkId",
                table: "TeamLabTrafficFlows",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficFlows_RuntimeId_CapturedAt",
                table: "TeamLabTrafficFlows",
                columns: new[] { "RuntimeId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficFlows_ShardId_CapturedAt",
                table: "TeamLabTrafficFlows",
                columns: new[] { "ShardId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficFlows_WorkerNodeId",
                table: "TeamLabTrafficFlows",
                column: "WorkerNodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimeAssets_TeamLabRuntimeShards_ShardId",
                table: "TeamLabRuntimeAssets",
                column: "ShardId",
                principalTable: "TeamLabRuntimeShards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimeAssets_WorkerNodes_WorkerNodeId",
                table: "TeamLabRuntimeAssets",
                column: "WorkerNodeId",
                principalTable: "WorkerNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimeNetworks_TeamLabRuntimeShards_ShardId",
                table: "TeamLabRuntimeNetworks",
                column: "ShardId",
                principalTable: "TeamLabRuntimeShards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimeNetworks_WorkerNodes_WorkerNodeId",
                table: "TeamLabRuntimeNetworks",
                column: "WorkerNodeId",
                principalTable: "WorkerNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimeAssets_TeamLabRuntimeShards_ShardId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimeAssets_WorkerNodes_WorkerNodeId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimeNetworks_TeamLabRuntimeShards_ShardId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimeNetworks_WorkerNodes_WorkerNodeId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropTable(
                name: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropTable(
                name: "TeamLabTrafficFlows");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeShards");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeNetworks_ShardId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeNetworks_WorkerNodeId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeAssets_ShardId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeAssets_WorkerNodeId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "TeamLabAgentVersion",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabCapabilitiesJson",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabFabricIp",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabFabricStatus",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabProtocolVersion",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "ShardId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropColumn(
                name: "WorkerNodeId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropColumn(
                name: "ShardId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "WorkerNodeId",
                table: "TeamLabRuntimeAssets");
        }
    }
}
