using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class FinalAuditCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IREnabled",
                table: "GamePhases");

            migrationBuilder.DropColumn(
                name: "ScenarioEnabled",
                table: "GamePhases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IREnabled",
                table: "GamePhases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ScenarioEnabled",
                table: "GamePhases",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
