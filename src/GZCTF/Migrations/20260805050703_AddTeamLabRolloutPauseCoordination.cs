using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabRolloutPauseCoordination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PauseRequested",
                table: "TeamLabRollouts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueueTickets_TeamLabRuntimeId_Operation_CreatedAt",
                table: "DeploymentQueueTickets",
                columns: new[] { "TeamLabRuntimeId", "Operation", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeploymentQueueTickets_TeamLabRuntimeId_Operation_CreatedAt",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "PauseRequested",
                table: "TeamLabRollouts");
        }
    }
}
