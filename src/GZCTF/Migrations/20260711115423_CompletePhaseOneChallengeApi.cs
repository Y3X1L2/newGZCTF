using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class CompletePhaseOneChallengeApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Games",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChallengeMutationJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeMutationJobs", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_ChallengeMutationJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengeMutationJobs_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_OwnerId",
                table: "Games",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeMutationJobs_GameId",
                table: "ChallengeMutationJobs",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeMutationJobs_Kind_CompletedAt",
                table: "ChallengeMutationJobs",
                columns: new[] { "Kind", "CompletedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Games_AspNetUsers_OwnerId",
                table: "Games",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_AspNetUsers_OwnerId",
                table: "Games");

            migrationBuilder.DropTable(
                name: "ChallengeMutationJobs");

            migrationBuilder.DropIndex(
                name: "IX_Games_OwnerId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Games");
        }
    }
}
