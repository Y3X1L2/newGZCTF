using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddVmInstanceAccessFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuacamoleConnectionId",
                table: "VmInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "VmInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RdpPassword",
                table: "VmInstances",
                type: "text",
                nullable: false,
                defaultValue: "qwer1234!");

            migrationBuilder.AddColumn<string>(
                name: "RdpUrl",
                table: "VmInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RdpUsername",
                table: "VmInstances",
                type: "text",
                nullable: false,
                defaultValue: "player");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuacamoleConnectionId",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "RdpPassword",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "RdpUrl",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "RdpUsername",
                table: "VmInstances");
        }
    }
}
