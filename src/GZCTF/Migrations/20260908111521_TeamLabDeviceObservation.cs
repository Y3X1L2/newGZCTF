using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class TeamLabDeviceObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeviceNextProbeAt",
                table: "TeamLabRuntimeAssets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceObservationJson",
                table: "TeamLabRuntimeAssets",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceNextProbeAt",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "DeviceObservationJson",
                table: "TeamLabRuntimeAssets");
        }
    }
}
