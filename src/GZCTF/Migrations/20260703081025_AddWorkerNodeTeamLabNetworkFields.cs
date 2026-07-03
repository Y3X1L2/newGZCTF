using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerNodeTeamLabNetworkFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TeamLabNetworkEnabled",
                table: "WorkerNodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TeamLabTunnelConfigVersion",
                table: "WorkerNodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TeamLabTunnelIp",
                table: "WorkerNodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamLabTunnelLastError",
                table: "WorkerNodes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TeamLabTunnelLastHandshake",
                table: "WorkerNodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "TeamLabTunnelStatus",
                table: "WorkerNodes",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeamLabNetworkEnabled",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabTunnelConfigVersion",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabTunnelIp",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabTunnelLastError",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabTunnelLastHandshake",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "TeamLabTunnelStatus",
                table: "WorkerNodes");
        }
    }
}
