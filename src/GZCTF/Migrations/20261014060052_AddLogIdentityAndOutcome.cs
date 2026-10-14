using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddLogIdentityAndOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DetailJson",
                table: "OperationalEvents",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorKind",
                table: "Logs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "Logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetailJson",
                table: "Logs",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "Logs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Retryable",
                table: "Logs",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActorKind",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "DetailJson",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Retryable",
                table: "Logs");

            migrationBuilder.AlterColumn<string>(
                name: "DetailJson",
                table: "OperationalEvents",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8192)",
                oldMaxLength: 8192,
                oldNullable: true);
        }
    }
}
