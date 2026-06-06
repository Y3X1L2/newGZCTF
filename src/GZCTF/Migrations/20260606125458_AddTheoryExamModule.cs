using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTheoryExamModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TheoryPapers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryPapers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TheoryPapers_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TheoryQuestionBankItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: false),
                    AnswerIndexes = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryQuestionBankItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TheoryAnswerSheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    MaxScore = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    PaperId = table.Column<int>(type: "integer", nullable: false),
                    ParticipationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryAnswerSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TheoryAnswerSheets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheoryAnswerSheets_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheoryAnswerSheets_Participations_ParticipationId",
                        column: x => x.ParticipationId,
                        principalTable: "Participations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheoryAnswerSheets_TheoryPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "TheoryPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TheoryPaperQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaperId = table.Column<int>(type: "integer", nullable: false),
                    SourceQuestionId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: false),
                    AnswerIndexes = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryPaperQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TheoryPaperQuestions_TheoryPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "TheoryPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheoryPaperQuestions_TheoryQuestionBankItems_SourceQuestionId",
                        column: x => x.SourceQuestionId,
                        principalTable: "TheoryQuestionBankItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TheorySubmissionAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AnswerSheetId = table.Column<int>(type: "integer", nullable: false),
                    PaperQuestionId = table.Column<int>(type: "integer", nullable: false),
                    SelectedIndexes = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheorySubmissionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TheorySubmissionAnswers_TheoryAnswerSheets_AnswerSheetId",
                        column: x => x.AnswerSheetId,
                        principalTable: "TheoryAnswerSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheorySubmissionAnswers_TheoryPaperQuestions_PaperQuestionId",
                        column: x => x.PaperQuestionId,
                        principalTable: "TheoryPaperQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TheoryAnswerSheets_GameId",
                table: "TheoryAnswerSheets",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryAnswerSheets_PaperId",
                table: "TheoryAnswerSheets",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryAnswerSheets_ParticipationId",
                table: "TheoryAnswerSheets",
                column: "ParticipationId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryAnswerSheets_UserId_GameId",
                table: "TheoryAnswerSheets",
                columns: new[] { "UserId", "GameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TheoryPaperQuestions_PaperId",
                table: "TheoryPaperQuestions",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryPaperQuestions_SourceQuestionId",
                table: "TheoryPaperQuestions",
                column: "SourceQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryPapers_GameId",
                table: "TheoryPapers",
                column: "GameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TheoryQuestionBankItems_Type",
                table: "TheoryQuestionBankItems",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_TheorySubmissionAnswers_AnswerSheetId",
                table: "TheorySubmissionAnswers",
                column: "AnswerSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_TheorySubmissionAnswers_PaperQuestionId",
                table: "TheorySubmissionAnswers",
                column: "PaperQuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TheorySubmissionAnswers");

            migrationBuilder.DropTable(
                name: "TheoryAnswerSheets");

            migrationBuilder.DropTable(
                name: "TheoryPaperQuestions");

            migrationBuilder.DropTable(
                name: "TheoryPapers");

            migrationBuilder.DropTable(
                name: "TheoryQuestionBankItems");
        }
    }
}
