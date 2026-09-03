using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260814075023_AddAssetAndChallengeOwnership")]
public partial class AddAssetAndChallengeOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CreatedById",
            table: "GameChallenges",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CreatedById",
            table: "Files",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CreatedById",
            table: "ExerciseChallenges",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_GameChallenges_CreatedById",
            table: "GameChallenges",
            column: "CreatedById");

        migrationBuilder.CreateIndex(
            name: "IX_Files_CreatedById",
            table: "Files",
            column: "CreatedById");

        migrationBuilder.CreateIndex(
            name: "IX_ExerciseChallenges_CreatedById",
            table: "ExerciseChallenges",
            column: "CreatedById");

        migrationBuilder.AddForeignKey(
            name: "FK_ExerciseChallenges_AspNetUsers_CreatedById",
            table: "ExerciseChallenges",
            column: "CreatedById",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Files_AspNetUsers_CreatedById",
            table: "Files",
            column: "CreatedById",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_GameChallenges_AspNetUsers_CreatedById",
            table: "GameChallenges",
            column: "CreatedById",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ExerciseChallenges_AspNetUsers_CreatedById",
            table: "ExerciseChallenges");

        migrationBuilder.DropForeignKey(
            name: "FK_Files_AspNetUsers_CreatedById",
            table: "Files");

        migrationBuilder.DropForeignKey(
            name: "FK_GameChallenges_AspNetUsers_CreatedById",
            table: "GameChallenges");

        migrationBuilder.DropIndex(
            name: "IX_GameChallenges_CreatedById",
            table: "GameChallenges");

        migrationBuilder.DropIndex(
            name: "IX_Files_CreatedById",
            table: "Files");

        migrationBuilder.DropIndex(
            name: "IX_ExerciseChallenges_CreatedById",
            table: "ExerciseChallenges");

        migrationBuilder.DropColumn(
            name: "CreatedById",
            table: "GameChallenges");

        migrationBuilder.DropColumn(
            name: "CreatedById",
            table: "Files");

        migrationBuilder.DropColumn(
            name: "CreatedById",
            table: "ExerciseChallenges");
    }
}
