using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddGameChallengeCreatorTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "GameChallenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameChallenges_CreatedById",
                table: "GameChallenges",
                column: "CreatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_GameChallenges_AspNetUsers_CreatedById",
                table: "GameChallenges",
                column: "CreatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameChallenges_AspNetUsers_CreatedById",
                table: "GameChallenges");

            migrationBuilder.DropIndex(
                name: "IX_GameChallenges_CreatedById",
                table: "GameChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "GameChallenges");
        }
    }
}
