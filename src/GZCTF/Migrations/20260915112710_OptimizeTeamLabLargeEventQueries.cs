using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeTeamLabLargeEventQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_Scope_Status_Created_PublicId",
                table: "TeamLabRuntimes",
                columns: new[] { "ControlScopeId", "Status", "CreatedAt", "PublicId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_Runtime_Generation_Status_Id",
                table: "TeamLabRuntimeAssets",
                columns: new[] { "RuntimeId", "Generation", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_Runtime_Generation_Status_Created_Id",
                table: "TeamLabRemoteSessions",
                columns: new[] { "RuntimeId", "Generation", "Status", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabEvents_Runtime_Generation_Stage_Created_Id",
                table: "TeamLabEvents",
                columns: new[] { "RuntimeId", "Generation", "Stage", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueueTickets_TeamLabRuntime_Generation_Operation_Created_Id",
                table: "DeploymentQueueTickets",
                columns: new[] { "TeamLabRuntimeId", "Generation", "Operation", "CreatedAt", "Id" },
                filter: "\"TeamLabRuntimeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_Scope_Status_Created_PublicId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeAssets_Runtime_Generation_Status_Id",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRemoteSessions_Runtime_Generation_Status_Created_Id",
                table: "TeamLabRemoteSessions");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabEvents_Runtime_Generation_Stage_Created_Id",
                table: "TeamLabEvents");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentQueueTickets_TeamLabRuntime_Generation_Operation_Created_Id",
                table: "DeploymentQueueTickets");
        }
    }
}
