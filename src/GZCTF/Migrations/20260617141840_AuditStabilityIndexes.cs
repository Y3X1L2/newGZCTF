using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AuditStabilityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ChallengeId_UserId_SubmissionType",
                table: "Submissions",
                columns: new[] { "ChallengeId", "UserId", "SubmissionType" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ParticipationId_ChallengeId",
                table: "Submissions",
                columns: new[] { "ParticipationId", "ChallengeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_SubmissionType",
                table: "Submissions",
                column: "SubmissionType");

            migrationBuilder.CreateIndex(
                name: "IX_Participations_GameId_Status",
                table: "Participations",
                columns: new[] { "GameId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FirstSolves_ParticipationId_ChallengeId",
                table: "FirstSolves",
                columns: new[] { "ParticipationId", "ChallengeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_ChallengeId_UserId_SubmissionType",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_ParticipationId_ChallengeId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_SubmissionType",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Participations_GameId_Status",
                table: "Participations");

            migrationBuilder.DropIndex(
                name: "IX_FirstSolves_ParticipationId_ChallengeId",
                table: "FirstSolves");
        }
    }
}
