using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabManagedGuestNetwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DnsServersJson",
                table: "TeamLabTopologyInterfaces",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestInterfaceName",
                table: "TeamLabTopologyInterfaces",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaticRoutesJson",
                table: "TeamLabTopologyInterfaces",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseDefaultGateway",
                table: "TeamLabTopologyInterfaces",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "VmNetworkMode",
                table: "TeamLabTopologyAssets",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DnsServersJson",
                table: "TeamLabTopologyInterfaces");

            migrationBuilder.DropColumn(
                name: "GuestInterfaceName",
                table: "TeamLabTopologyInterfaces");

            migrationBuilder.DropColumn(
                name: "StaticRoutesJson",
                table: "TeamLabTopologyInterfaces");

            migrationBuilder.DropColumn(
                name: "UseDefaultGateway",
                table: "TeamLabTopologyInterfaces");

            migrationBuilder.DropColumn(
                name: "VmNetworkMode",
                table: "TeamLabTopologyAssets");
        }
    }
}
