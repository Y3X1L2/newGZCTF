using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabVpnPeerSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedClientPrivateKey",
                table: "TeamLabVpnPeerRuntimes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedServerPrivateKey",
                table: "TeamLabVpnPeerRuntimes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ServerPublicKey",
                table: "TeamLabVpnPeerRuntimes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectedClientPrivateKey",
                table: "TeamLabVpnPeerRuntimes");

            migrationBuilder.DropColumn(
                name: "ProtectedServerPrivateKey",
                table: "TeamLabVpnPeerRuntimes");

            migrationBuilder.DropColumn(
                name: "ServerPublicKey",
                table: "TeamLabVpnPeerRuntimes");
        }
    }
}
