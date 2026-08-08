using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabScopedWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RuntimeId",
                table: "TeamLabEvents",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid>(
                name: "ControlScopeId",
                table: "TeamLabEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "TeamLabEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResourcePublicId",
                table: "TeamLabEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                table: "TeamLabEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceUrl",
                table: "TeamLabEvents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResourceVersion",
                table: "TeamLabEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "TeamLabEvents" AS event
                SET "ControlScopeId" = runtime."ControlScopeId",
                    "ResourceType" = 'teamlab-runtime',
                    "ResourcePublicId" = runtime."PublicId",
                    "ResourceVersion" = event."Generation",
                    "ResourceUrl" = '/api/open/v1/teamlab/runtimes/' || runtime."PublicId"::text
                FROM "TeamLabRuntimes" AS runtime
                WHERE event."RuntimeId" = runtime."Id"
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabEvents_Operation",
                table: "TeamLabEvents",
                column: "OperationId",
                filter: "\"OperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabEvents_Scope_Cursor",
                table: "TeamLabEvents",
                columns: new[] { "ControlScopeId", "Id" },
                filter: "\"ControlScopeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabEvents_Operation",
                table: "TeamLabEvents");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabEvents_Scope_Cursor",
                table: "TeamLabEvents");

            migrationBuilder.DropColumn(
                name: "ControlScopeId",
                table: "TeamLabEvents");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "TeamLabEvents");

            migrationBuilder.DropColumn(
                name: "ResourcePublicId",
                table: "TeamLabEvents");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "TeamLabEvents");

            migrationBuilder.DropColumn(
                name: "ResourceUrl",
                table: "TeamLabEvents");

            migrationBuilder.DropColumn(
                name: "ResourceVersion",
                table: "TeamLabEvents");

            migrationBuilder.AlterColumn<int>(
                name: "RuntimeId",
                table: "TeamLabEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
