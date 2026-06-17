using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingChapterTheoryHomework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrainingCourseChapterTheoryPapers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    ChapterId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    PassRate = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseChapterTheoryPapers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheoryPapers_AspNetUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheoryPapers_TrainingCourseChapters_Ch~",
                        column: x => x.ChapterId,
                        principalTable: "TrainingCourseChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheoryPapers_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseTheoryQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BankName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: false),
                    AnswerIndexes = table.Column<string>(type: "text", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseTheoryQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseTheoryQuestions_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseTheoryQuestions_AspNetUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseTheoryQuestions_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseChapterTheorySheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    ChapterId = table.Column<int>(type: "integer", nullable: false),
                    PaperId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    MaxScore = table.Column<int>(type: "integer", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseChapterTheorySheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheorySheets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheorySheets_TrainingCourseChapterTheo~",
                        column: x => x.PaperId,
                        principalTable: "TrainingCourseChapterTheoryPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheorySheets_TrainingCourseChapters_Ch~",
                        column: x => x.ChapterId,
                        principalTable: "TrainingCourseChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheorySheets_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseChapterTheoryQuestions",
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
                    table.PrimaryKey("PK_TrainingCourseChapterTheoryQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheoryQuestions_TrainingCourseChapterT~",
                        column: x => x.PaperId,
                        principalTable: "TrainingCourseChapterTheoryPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheoryQuestions_TrainingCourseTheoryQu~",
                        column: x => x.SourceQuestionId,
                        principalTable: "TrainingCourseTheoryQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseChapterTheoryAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SheetId = table.Column<int>(type: "integer", nullable: false),
                    PaperQuestionId = table.Column<int>(type: "integer", nullable: false),
                    SelectedIndexes = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseChapterTheoryAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheoryAnswers_TrainingCourseChapterThe~",
                        column: x => x.PaperQuestionId,
                        principalTable: "TrainingCourseChapterTheoryQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterTheoryAnswers_TrainingCourseChapterTh~1",
                        column: x => x.SheetId,
                        principalTable: "TrainingCourseChapterTheorySheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheoryAnswers_PaperQuestionId",
                table: "TrainingCourseChapterTheoryAnswers",
                column: "PaperQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheoryAnswers_SheetId",
                table: "TrainingCourseChapterTheoryAnswers",
                column: "SheetId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheoryPapers_ChapterId",
                table: "TrainingCourseChapterTheoryPapers",
                column: "ChapterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheoryPapers_CourseId",
                table: "TrainingCourseChapterTheoryPapers",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheoryPapers_UpdatedById",
                table: "TrainingCourseChapterTheoryPapers",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheoryQuestions_PaperId",
                table: "TrainingCourseChapterTheoryQuestions",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheoryQuestions_SourceQuestionId",
                table: "TrainingCourseChapterTheoryQuestions",
                column: "SourceQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheorySheets_ChapterId",
                table: "TrainingCourseChapterTheorySheets",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheorySheets_CourseId",
                table: "TrainingCourseChapterTheorySheets",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheorySheets_PaperId",
                table: "TrainingCourseChapterTheorySheets",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterTheorySheets_UserId_ChapterId",
                table: "TrainingCourseChapterTheorySheets",
                columns: new[] { "UserId", "ChapterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseTheoryQuestions_CourseId_Type_BankName",
                table: "TrainingCourseTheoryQuestions",
                columns: new[] { "CourseId", "Type", "BankName" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseTheoryQuestions_CreatedById",
                table: "TrainingCourseTheoryQuestions",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseTheoryQuestions_UpdatedById",
                table: "TrainingCourseTheoryQuestions",
                column: "UpdatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainingCourseChapterTheoryAnswers");

            migrationBuilder.DropTable(
                name: "TrainingCourseChapterTheoryQuestions");

            migrationBuilder.DropTable(
                name: "TrainingCourseChapterTheorySheets");

            migrationBuilder.DropTable(
                name: "TrainingCourseTheoryQuestions");

            migrationBuilder.DropTable(
                name: "TrainingCourseChapterTheoryPapers");
        }
    }
}
