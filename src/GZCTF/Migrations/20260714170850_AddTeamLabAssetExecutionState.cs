using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabAssetExecutionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BootstrapDigest",
                table: "TeamLabRuntimeAssets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "EndpointObservation",
                table: "TeamLabRuntimeAssets",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "ExecutionStage",
                table: "TeamLabRuntimeAssets",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutionUpdatedAt",
                table: "TeamLabRuntimeAssets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageDigest",
                table: "TeamLabRuntimeAssets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Stateless",
                table: "TeamLabRuntimeAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootstrapDigest",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "EndpointObservation",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "ExecutionStage",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "ExecutionUpdatedAt",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "ImageDigest",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "Stateless",
                table: "TeamLabRuntimeAssets");
        }
    }
}
