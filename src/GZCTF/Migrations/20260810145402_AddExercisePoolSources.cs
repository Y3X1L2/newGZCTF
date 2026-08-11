using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddExercisePoolSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "MinimumVisibleRole",
                table: "ExerciseChallenges",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "PoolSource",
                table: "ExerciseChallenges",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "SourceChallengeId",
                table: "ExerciseChallenges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceGameId",
                table: "ExerciseChallenges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceTrainingCourseId",
                table: "ExerciseChallenges",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinimumVisibleRole",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "PoolSource",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "SourceChallengeId",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "SourceGameId",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "SourceTrainingCourseId",
                table: "ExerciseChallenges");
        }
    }
}
