using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabRollouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PenetrationGameLabBindings_TopologyId",
                table: "PenetrationGameLabBindings");

            migrationBuilder.AddColumn<int>(
                name: "ActiveRolloutId",
                table: "PenetrationGameLabBindings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamLabRollouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdapterKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    PreparationRequested = table.Column<bool>(type: "boolean", nullable: false),
                    DesiredAccessOpen = table.Column<bool>(type: "boolean", nullable: false),
                    DrainRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PreparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessOpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DrainingAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRollouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRollouts_TeamLabTopologyReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "TeamLabTopologyReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRolloutTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RolloutId = table.Column<int>(type: "integer", nullable: false),
                    ExternalSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    LastOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DestroyedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRolloutTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRolloutTargets_TeamLabRollouts_RolloutId",
                        column: x => x.RolloutId,
                        principalTable: "TeamLabRollouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRolloutTargets_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationGameLabBindings_ActiveRolloutId",
                table: "PenetrationGameLabBindings",
                column: "ActiveRolloutId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationGameLabBindings_TopologyId",
                table: "PenetrationGameLabBindings",
                column: "TopologyId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRollouts_AdapterKind_ExternalReference_ReleaseId",
                table: "TeamLabRollouts",
                columns: new[] { "AdapterKind", "ExternalReference", "ReleaseId" },
                unique: true,
                filter: "\"Status\" <> 5");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRollouts_PublicId",
                table: "TeamLabRollouts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRollouts_ReleaseId",
                table: "TeamLabRollouts",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRollouts_Status_UpdatedAt",
                table: "TeamLabRollouts",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRolloutTargets_PublicId",
                table: "TeamLabRolloutTargets",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRolloutTargets_RolloutId_ExternalSubject",
                table: "TeamLabRolloutTargets",
                columns: new[] { "RolloutId", "ExternalSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRolloutTargets_RolloutId_Status_Id",
                table: "TeamLabRolloutTargets",
                columns: new[] { "RolloutId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRolloutTargets_RuntimeId",
                table: "TeamLabRolloutTargets",
                column: "RuntimeId",
                unique: true,
                filter: "\"RuntimeId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationGameLabBindings_TeamLabRollouts_ActiveRolloutId",
                table: "PenetrationGameLabBindings",
                column: "ActiveRolloutId",
                principalTable: "TeamLabRollouts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationGameLabBindings_TeamLabRollouts_ActiveRolloutId",
                table: "PenetrationGameLabBindings");

            migrationBuilder.DropTable(
                name: "TeamLabRolloutTargets");

            migrationBuilder.DropTable(
                name: "TeamLabRollouts");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationGameLabBindings_ActiveRolloutId",
                table: "PenetrationGameLabBindings");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationGameLabBindings_TopologyId",
                table: "PenetrationGameLabBindings");

            migrationBuilder.DropColumn(
                name: "ActiveRolloutId",
                table: "PenetrationGameLabBindings");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationGameLabBindings_TopologyId",
                table: "PenetrationGameLabBindings",
                column: "TopologyId",
                unique: true);
        }
    }
}
