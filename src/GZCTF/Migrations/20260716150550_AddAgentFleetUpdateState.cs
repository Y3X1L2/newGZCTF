using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentFleetUpdateState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgentUpdateCompletedAt",
                table: "WorkerNodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentUpdateExpectedSha256",
                table: "WorkerNodes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentUpdateLastError",
                table: "WorkerNodes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgentUpdateStartedAt",
                table: "WorkerNodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "AgentUpdateState",
                table: "WorkerNodes",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "AgentUpdateWasSchedulable",
                table: "WorkerNodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentUpdateCompletedAt",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "AgentUpdateExpectedSha256",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "AgentUpdateLastError",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "AgentUpdateStartedAt",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "AgentUpdateState",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "AgentUpdateWasSchedulable",
                table: "WorkerNodes");
        }
    }
}
