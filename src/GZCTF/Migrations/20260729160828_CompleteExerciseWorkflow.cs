using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class CompleteExerciseWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExerciseMutationJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ExerciseId = table.Column<int>(type: "integer", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseMutationJobs", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_ExerciseMutationJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseMutationJobs_ExerciseChallenges_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "ExerciseChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseSubmissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExerciseChallengeId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAnswerHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FlagId = table.Column<int>(type: "integer", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseSubmissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseSubmissions_ExerciseChallenges_ExerciseChallengeId",
                        column: x => x.ExerciseChallengeId,
                        principalTable: "ExerciseChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseSubmissions_FlagContexts_FlagId",
                        column: x => x.FlagId,
                        principalTable: "FlagContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseMutationJobs_ExerciseId",
                table: "ExerciseMutationJobs",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseMutationJobs_Kind_CompletedAt",
                table: "ExerciseMutationJobs",
                columns: new[] { "Kind", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseSubmissions_ExerciseChallengeId_UserId",
                table: "ExerciseSubmissions",
                columns: new[] { "ExerciseChallengeId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseSubmissions_FlagId",
                table: "ExerciseSubmissions",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseSubmissions_UserId_SubmittedAt",
                table: "ExerciseSubmissions",
                columns: new[] { "UserId", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExerciseMutationJobs");

            migrationBuilder.DropTable(
                name: "ExerciseSubmissions");
        }
    }
}
