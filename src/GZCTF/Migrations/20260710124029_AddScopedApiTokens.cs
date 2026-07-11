using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedApiTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    token_count bigint;
                    ownerless_count bigint;
                BEGIN
                    SELECT count(*) INTO token_count FROM "ApiTokens";
                    SELECT count(*) INTO ownerless_count FROM "ApiTokens" WHERE "CreatorId" IS NULL;
                    RAISE NOTICE 'Phase 1 API token migration: total=%, ownerless=%', token_count, ownerless_count;
                    DELETE FROM "ApiTokens" WHERE "CreatorId" IS NULL;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "IsRevoked",
                table: "ApiTokens");

            migrationBuilder.AlterTable(
                name: "ApiTokens",
                oldComment: "Stores API tokens for programmatic access.");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ApiTokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "A user-friendly name for the token.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastUsedAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "The timestamp when the token was last used.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "The timestamp when the token expires. A null value means it never expires.");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatorId",
                table: "ApiTokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The ID of the user who created the token.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "The timestamp when the token was created.");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ApiTokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The unique identifier for the token.");

            migrationBuilder.AddColumn<int>(
                name: "RequestsPerMinute",
                table: "ApiTokens",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SecretHash",
                table: "ApiTokens",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ApiTokens"
                SET "RevokedAt" = clock_timestamp(),
                    "SecretHash" = decode(
                        md5(random()::text || clock_timestamp()::text || "Id"::text) ||
                        md5(random()::text || clock_timestamp()::text || "Name"),
                        'hex');
                """);

            migrationBuilder.AlterColumn<byte[]>(
                name: "SecretHash",
                table: "ApiTokens",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ApiTokenResourceGrants",
                columns: table => new
                {
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokenResourceGrants", x => new { x.TokenId, x.ResourceType, x.ResourceId });
                    table.ForeignKey(
                        name: "FK_ApiTokenResourceGrants_ApiTokens_TokenId",
                        column: x => x.TokenId,
                        principalTable: "ApiTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiTokenScopeGrants",
                columns: table => new
                {
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokenScopeGrants", x => new { x.TokenId, x.Scope });
                    table.ForeignKey(
                        name: "FK_ApiTokenScopeGrants_ApiTokens_TokenId",
                        column: x => x.TokenId,
                        principalTable: "ApiTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiTokenResourceGrants");

            migrationBuilder.DropTable(
                name: "ApiTokenScopeGrants");

            migrationBuilder.DropColumn(
                name: "RequestsPerMinute",
                table: "ApiTokens");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "ApiTokens");

            migrationBuilder.DropColumn(
                name: "SecretHash",
                table: "ApiTokens");

            migrationBuilder.AlterTable(
                name: "ApiTokens",
                comment: "Stores API tokens for programmatic access.");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ApiTokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "A user-friendly name for the token.",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastUsedAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The timestamp when the token was last used.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The timestamp when the token expires. A null value means it never expires.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatorId",
                table: "ApiTokens",
                type: "uuid",
                nullable: false,
                comment: "The ID of the user who created the token.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: false,
                comment: "The timestamp when the token was created.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ApiTokens",
                type: "uuid",
                nullable: false,
                comment: "The unique identifier for the token.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "IsRevoked",
                table: "ApiTokens",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Indicates whether the token has been revoked.");
        }
    }
}
