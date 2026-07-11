using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class FixFirstSolveFlagCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagId",
                table: "FirstSolves");

            migrationBuilder.AddForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagId",
                table: "FirstSolves",
                column: "FlagId",
                principalTable: "FlagContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagId",
                table: "FirstSolves");

            migrationBuilder.AddForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagId",
                table: "FirstSolves",
                column: "FlagId",
                principalTable: "FlagContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
