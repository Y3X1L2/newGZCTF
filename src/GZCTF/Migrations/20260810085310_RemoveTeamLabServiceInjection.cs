using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTeamLabServiceInjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabBootstrapExecutions");

            migrationBuilder.DropColumn(
                name: "BootstrapJson",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "BootstrapDigest",
                table: "TeamLabRuntimeAssets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BootstrapJson",
                table: "TeamLabTopologyAssets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BootstrapDigest",
                table: "TeamLabRuntimeAssets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamLabBootstrapExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssetId = table.Column<int>(type: "integer", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    BootEpoch = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    InputDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OutputDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    StepKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabBootstrapExecutions", x => x.Id);
                    table.CheckConstraint("CK_TeamLabBootstrapExecutions_Attempt", "\"Attempt\" = 1");
                    table.ForeignKey(
                        name: "FK_TeamLabBootstrapExecutions_TeamLabRuntimeAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "TeamLabRuntimeAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabBootstrapExecutions_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabBootstrapExecutions_AssetId",
                table: "TeamLabBootstrapExecutions",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabBootstrapExecutions_ExecutionId",
                table: "TeamLabBootstrapExecutions",
                column: "ExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabBootstrapExecutions_RuntimeId_Generation_AssetId_Pro~",
                table: "TeamLabBootstrapExecutions",
                columns: new[] { "RuntimeId", "Generation", "AssetId", "ProfileId", "ProfileVersion", "StepKey" },
                unique: true);
        }
    }
}
