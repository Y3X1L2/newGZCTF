using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AuditCriticalFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagContextId",
                table: "FirstSolves");

            migrationBuilder.DropForeignKey(
                name: "FK_Submissions_FlagContexts_FlagContextId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_FlagContextId",
                table: "Submissions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_FirstSolves",
                table: "FirstSolves");

            migrationBuilder.DropIndex(
                name: "IX_FirstSolves_FlagContextId",
                table: "FirstSolves");

            migrationBuilder.DropColumn(
                name: "FlagContextId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "FlagContextId",
                table: "FirstSolves");

            migrationBuilder.AddPrimaryKey(
                name: "PK_FirstSolves",
                table: "FirstSolves",
                columns: new[] { "ParticipationId", "ChallengeId", "FlagId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_FlagId",
                table: "Submissions",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_FirstSolves_FlagId",
                table: "FirstSolves",
                column: "FlagId");

            migrationBuilder.AddForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagId",
                table: "FirstSolves",
                column: "FlagId",
                principalTable: "FlagContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Submissions_FlagContexts_FlagId",
                table: "Submissions",
                column: "FlagId",
                principalTable: "FlagContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagId",
                table: "FirstSolves");

            migrationBuilder.DropForeignKey(
                name: "FK_Submissions_FlagContexts_FlagId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_FlagId",
                table: "Submissions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_FirstSolves",
                table: "FirstSolves");

            migrationBuilder.DropIndex(
                name: "IX_FirstSolves_FlagId",
                table: "FirstSolves");

            migrationBuilder.AddColumn<int>(
                name: "FlagContextId",
                table: "Submissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlagContextId",
                table: "FirstSolves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_FirstSolves",
                table: "FirstSolves",
                columns: new[] { "ParticipationId", "ChallengeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_FlagContextId",
                table: "Submissions",
                column: "FlagContextId");

            migrationBuilder.CreateIndex(
                name: "IX_FirstSolves_FlagContextId",
                table: "FirstSolves",
                column: "FlagContextId");

            migrationBuilder.AddForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagContextId",
                table: "FirstSolves",
                column: "FlagContextId",
                principalTable: "FlagContexts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Submissions_FlagContexts_FlagContextId",
                table: "Submissions",
                column: "FlagContextId",
                principalTable: "FlagContexts",
                principalColumn: "Id");
        }
    }
}
