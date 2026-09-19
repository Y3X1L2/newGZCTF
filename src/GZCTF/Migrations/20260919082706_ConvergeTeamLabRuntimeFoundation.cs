using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class ConvergeTeamLabRuntimeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeGrants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Permissions = table.Column<int>(type: "integer", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeGrants", x => x.Id);
                    table.CheckConstraint("CK_TeamLabRuntimeGrants_Subject", "(\"UserId\" IS NULL) <> (\"ApiTokenId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeGrants_ApiTokens_ApiTokenId",
                        column: x => x.ApiTokenId,
                        principalTable: "ApiTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeGrants_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeGrants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeGrants_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeGrants_ApiTokenId",
                table: "TeamLabRuntimeGrants",
                column: "ApiTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeGrants_GrantedByUserId",
                table: "TeamLabRuntimeGrants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeGrants_RuntimeId_ApiTokenId_AssetKey",
                table: "TeamLabRuntimeGrants",
                columns: new[] { "RuntimeId", "ApiTokenId", "AssetKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeGrants_RuntimeId_UserId_AssetKey",
                table: "TeamLabRuntimeGrants",
                columns: new[] { "RuntimeId", "UserId", "AssetKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeGrants_UserId",
                table: "TeamLabRuntimeGrants",
                column: "UserId");

            migrationBuilder.Sql("""
                INSERT INTO "TeamLabRuntimeGrants"
                    ("RuntimeId", "UserId", "ApiTokenId", "AssetKey", "Permissions",
                     "GrantedByUserId", "CreatedAt", "UpdatedAt")
                SELECT binding."RuntimeId", grant_row."UserId", NULL, NULL,
                       (CASE WHEN (grant_row."Permissions"::integer & 1) <> 0 THEN 1 ELSE 0 END) |
                       (CASE WHEN (grant_row."Permissions"::integer & 2) <> 0 THEN 4 ELSE 0 END),
                       grant_row."GrantedByUserId", grant_row."CreatedAt", grant_row."UpdatedAt"
                FROM "PenetrationTeamLabOperatorGrants" grant_row
                JOIN "PenetrationTeamRuntimeBindings" binding
                  ON binding."GameId" = grant_row."GameId"
                WHERE grant_row."Permissions" <> 0;
                """);

            migrationBuilder.Sql("""
                UPDATE "TeamLabRuntimeGrants" existing
                SET "Permissions" = 255,
                    "UpdatedAt" = GREATEST(existing."UpdatedAt", binding."CreatedAt")
                FROM "PenetrationTeamRuntimeBindings" binding
                JOIN "Games" game ON game."Id" = binding."GameId"
                WHERE existing."RuntimeId" = binding."RuntimeId"
                  AND existing."UserId" = game."OwnerId"
                  AND existing."AssetKey" IS NULL;

                INSERT INTO "TeamLabRuntimeGrants"
                    ("RuntimeId", "UserId", "ApiTokenId", "AssetKey", "Permissions",
                     "GrantedByUserId", "CreatedAt", "UpdatedAt")
                SELECT binding."RuntimeId", game."OwnerId", NULL, NULL, 255,
                       game."OwnerId", binding."CreatedAt", binding."CreatedAt"
                FROM "PenetrationTeamRuntimeBindings" binding
                JOIN "Games" game ON game."Id" = binding."GameId"
                WHERE game."OwnerId" IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM "TeamLabRuntimeGrants" existing
                    WHERE existing."RuntimeId" = binding."RuntimeId"
                      AND existing."UserId" = game."OwnerId"
                      AND existing."AssetKey" IS NULL
                );
                """);

            migrationBuilder.DropTable(
                name: "PenetrationTeamLabOperatorGrants");

            migrationBuilder.DropColumn(
                name: "IsScenarioBuild",
                table: "TeamLabRuntimes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabRuntimeGrants");

            migrationBuilder.AddColumn<bool>(
                name: "IsScenarioBuild",
                table: "TeamLabRuntimes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PenetrationTeamLabOperatorGrants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Permissions = table.Column<byte>(type: "smallint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationTeamLabOperatorGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamLabOperatorGrants_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamLabOperatorGrants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamLabOperatorGrants_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamLabOperatorGrants_GameId_UserId",
                table: "PenetrationTeamLabOperatorGrants",
                columns: new[] { "GameId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamLabOperatorGrants_GrantedByUserId",
                table: "PenetrationTeamLabOperatorGrants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamLabOperatorGrants_UserId",
                table: "PenetrationTeamLabOperatorGrants",
                column: "UserId");
        }
    }
}
