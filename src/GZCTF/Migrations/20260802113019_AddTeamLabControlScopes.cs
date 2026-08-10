using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabControlScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ControlScopeId",
                table: "TeamLabTopologyReleases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ControlScopeId",
                table: "TeamLabTopologies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ControlScopeId",
                table: "TeamLabRuntimes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ControlScopeId",
                table: "TeamLabRollouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamLabControlScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabControlScopes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyReleases_ControlScopeId",
                table: "TeamLabTopologyReleases",
                column: "ControlScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologies_ControlScopeId",
                table: "TeamLabTopologies",
                column: "ControlScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_ControlScopeId",
                table: "TeamLabRuntimes",
                column: "ControlScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRollouts_ControlScopeId",
                table: "TeamLabRollouts",
                column: "ControlScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabControlScopes_IsArchived_UpdatedAt",
                table: "TeamLabControlScopes",
                columns: new[] { "IsArchived", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabControlScopes_Key",
                table: "TeamLabControlScopes",
                column: "Key",
                unique: true);

            // The platform scope is the safe ownership home for all historical
            // TeamLab resources. New scopes are created through the application
            // service; migration must remain deterministic and restart-safe.
            migrationBuilder.Sql("""
                INSERT INTO "TeamLabControlScopes" ("Id", "Key", "DisplayName", "IsArchived", "CreatedAt", "UpdatedAt")
                VALUES ('00000000-0000-7000-8000-000000000001', 'platform', 'Platform', FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                ON CONFLICT ("Key") DO NOTHING;

                UPDATE "TeamLabTopologies"
                SET "ControlScopeId" = '00000000-0000-7000-8000-000000000001'
                WHERE "ControlScopeId" IS NULL;

                UPDATE "TeamLabTopologyReleases" AS release
                SET "ControlScopeId" = topology."ControlScopeId"
                FROM "TeamLabTopologies" AS topology
                WHERE release."TopologyId" = topology."Id"
                  AND release."ControlScopeId" IS NULL;

                UPDATE "TeamLabRuntimes" AS runtime
                SET "ControlScopeId" = release."ControlScopeId"
                FROM "TeamLabTopologyReleases" AS release
                WHERE runtime."TopologyReleaseId" = release."Id"
                  AND runtime."ControlScopeId" IS NULL;

                UPDATE "TeamLabRollouts" AS rollout
                SET "ControlScopeId" = release."ControlScopeId"
                FROM "TeamLabTopologyReleases" AS release
                WHERE rollout."ReleaseId" = release."Id"
                  AND rollout."ControlScopeId" IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRollouts_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabRollouts",
                column: "ControlScopeId",
                principalTable: "TeamLabControlScopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimes_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabRuntimes",
                column: "ControlScopeId",
                principalTable: "TeamLabControlScopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTopologies_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabTopologies",
                column: "ControlScopeId",
                principalTable: "TeamLabControlScopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTopologyReleases_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabTopologyReleases",
                column: "ControlScopeId",
                principalTable: "TeamLabControlScopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRollouts_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabRollouts");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimes_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTopologies_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabTopologies");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTopologyReleases_TeamLabControlScopes_ControlScopeId",
                table: "TeamLabTopologyReleases");

            migrationBuilder.DropTable(
                name: "TeamLabControlScopes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTopologyReleases_ControlScopeId",
                table: "TeamLabTopologyReleases");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTopologies_ControlScopeId",
                table: "TeamLabTopologies");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_ControlScopeId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRollouts_ControlScopeId",
                table: "TeamLabRollouts");

            migrationBuilder.DropColumn(
                name: "ControlScopeId",
                table: "TeamLabTopologyReleases");

            migrationBuilder.DropColumn(
                name: "ControlScopeId",
                table: "TeamLabTopologies");

            migrationBuilder.DropColumn(
                name: "ControlScopeId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "ControlScopeId",
                table: "TeamLabRollouts");
        }
    }
}
