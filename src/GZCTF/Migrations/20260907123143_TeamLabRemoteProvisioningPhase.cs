using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class TeamLabRemoteProvisioningPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GuacamoleCreationStarted",
                table: "TeamLabRemoteSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            migrationBuilder.Sql("""
                UPDATE "TeamLabRemoteSessions"
                SET "GuacamoleCreationStarted" = TRUE
                WHERE "Protocol" <> 1 AND "Status" IN (1, 2, 3, 4);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuacamoleCreationStarted",
                table: "TeamLabRemoteSessions");
        }
    }
}
