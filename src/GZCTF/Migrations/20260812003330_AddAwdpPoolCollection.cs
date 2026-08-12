using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdpPoolCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceAwdpServiceId",
                table: "ExerciseChallenges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Category",
                table: "AwdpServices",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "AwdpServices",
                type: "text",
                nullable: false,
                defaultValue: "flag{[GUID]}");

            migrationBuilder.AddColumn<byte>(
                name: "Difficulty",
                table: "AwdpServices",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "AwdpServices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlagTemplate",
                table: "AwdpServices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "AwdpServices",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseChallenges_PoolSource_SourceChallengeId_SourceAwdpS~",
                table: "ExerciseChallenges",
                columns: new[] { "PoolSource", "SourceChallengeId", "SourceAwdpServiceId" },
                filter: "\"TrainingCourseId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpServices_GameId_ExternalId",
                table: "AwdpServices",
                columns: new[] { "GameId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExerciseChallenges_PoolSource_SourceChallengeId_SourceAwdpS~",
                table: "ExerciseChallenges");

            migrationBuilder.DropIndex(
                name: "IX_AwdpServices_GameId_ExternalId",
                table: "AwdpServices");

            migrationBuilder.DropColumn(
                name: "SourceAwdpServiceId",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AwdpServices");

            migrationBuilder.DropColumn(
                name: "Content",
                table: "AwdpServices");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "AwdpServices");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "AwdpServices");

            migrationBuilder.DropColumn(
                name: "FlagTemplate",
                table: "AwdpServices");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "AwdpServices");
        }
    }
}
