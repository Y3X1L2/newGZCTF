using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class PersistTeamLabExecutionPlanSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLabExecutionPlanSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    ShardId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlanJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabExecutionPlanSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabExecutionPlanSnapshots_TeamLabRuntimeShards_ShardId",
                        column: x => x.ShardId,
                        principalTable: "TeamLabRuntimeShards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabExecutionPlanSnapshots_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabExecutionPlanSnapshots_RuntimeId_Generation_ShardId",
                table: "TeamLabExecutionPlanSnapshots",
                columns: new[] { "RuntimeId", "Generation", "ShardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabExecutionPlanSnapshots_ShardId",
                table: "TeamLabExecutionPlanSnapshots",
                column: "ShardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabExecutionPlanSnapshots");
        }
    }
}
