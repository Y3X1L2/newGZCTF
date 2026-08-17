using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class BindTeamLabCapabilityResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConnectorId",
                table: "TeamLabTopologyAssets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevicePackageId",
                table: "TeamLabTopologyAssets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DevicePackageParametersJson",
                table: "TeamLabTopologyAssets",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectorId",
                table: "TeamLabRuntimeAssets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevicePackageId",
                table: "TeamLabRuntimeAssets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DevicePackageParametersJson",
                table: "TeamLabRuntimeAssets",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyAssets_ConnectorId",
                table: "TeamLabTopologyAssets",
                column: "ConnectorId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyAssets_DevicePackageId",
                table: "TeamLabTopologyAssets",
                column: "DevicePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_DevicePackageId",
                table: "TeamLabRuntimeAssets",
                column: "DevicePackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTopologyAssets_TeamLabDevicePackages_DevicePackageId",
                table: "TeamLabTopologyAssets",
                column: "DevicePackageId",
                principalTable: "TeamLabDevicePackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTopologyAssets_TeamLabDevicePackages_DevicePackageId",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTopologyAssets_ConnectorId",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTopologyAssets_DevicePackageId",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeAssets_DevicePackageId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "ConnectorId",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "DevicePackageId",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "DevicePackageParametersJson",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "ConnectorId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "DevicePackageId",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "DevicePackageParametersJson",
                table: "TeamLabRuntimeAssets");
        }
    }
}
