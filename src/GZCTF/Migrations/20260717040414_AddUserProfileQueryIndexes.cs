using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_UserId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_UserId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_AwdpFlags_SubmittedByUserId",
                table: "AwdpFlags");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_User_Time_Challenge_Status",
                table: "Submissions",
                columns: new[] { "UserId", "SubmitTimeUtc", "ChallengeId", "Status" },
                descending: new[] { false, true, false, false });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_UserId_SubmittedAt",
                table: "PenetrationSubmissions",
                columns: new[] { "UserId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_SubmittedByUserId_FirstSubmittedAt",
                table: "AwdpFlags",
                columns: new[] { "SubmittedByUserId", "FirstSubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_User_Time_Challenge_Status",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_UserId_SubmittedAt",
                table: "PenetrationSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_AwdpFlags_SubmittedByUserId_FirstSubmittedAt",
                table: "AwdpFlags");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_UserId",
                table: "Submissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_UserId",
                table: "PenetrationSubmissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_SubmittedByUserId",
                table: "AwdpFlags",
                column: "SubmittedByUserId");
        }
    }
}
