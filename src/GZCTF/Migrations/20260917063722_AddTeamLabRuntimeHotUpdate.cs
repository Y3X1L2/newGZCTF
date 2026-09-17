using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabRuntimeHotUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlanRevision",
                table: "TeamLabRuntimes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionPlanJson",
                table: "TeamLabRuntimeAssets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentPlanJson",
                table: "TeamLabExecutionPlanSnapshots",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanRevision",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "ExecutionPlanJson",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "CurrentPlanJson",
                table: "TeamLabExecutionPlanSnapshots");
        }
    }
}
