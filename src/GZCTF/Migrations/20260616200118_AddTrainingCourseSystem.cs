using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingCourseSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrainingCourseId",
                table: "ImageTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrainingCourseId",
                table: "ExerciseChallenges",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrainingCourses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CoverFileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EnrollmentPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourses_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourses_AspNetUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseChallenges",
                columns: table => new
                {
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    ExerciseChallengeId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseChallenges", x => new { x.CourseId, x.ExerciseChallengeId });
                    table.ForeignKey(
                        name: "FK_TrainingCourseChallenges_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChallenges_ExerciseChallenges_ExerciseChallen~",
                        column: x => x.ExerciseChallengeId,
                        principalTable: "ExerciseChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChallenges_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseChapters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VideoProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    VideoFileId = table.Column<int>(type: "integer", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseChapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapters_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapters_AspNetUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapters_Files_VideoFileId",
                        column: x => x.VideoFileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapters_TrainingCourseChapters_ParentId",
                        column: x => x.ParentId,
                        principalTable: "TrainingCourseChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapters_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseEnrollments",
                columns: table => new
                {
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApplyReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ReviewComment = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseEnrollments", x => new { x.CourseId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TrainingCourseEnrollments_AspNetUsers_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseEnrollments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseEnrollments_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseProgresses",
                columns: table => new
                {
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CompletedChapterCount = table.Column<int>(type: "integer", nullable: false),
                    TotalChapterCount = table.Column<int>(type: "integer", nullable: false),
                    ChallengeSolvedCount = table.Column<int>(type: "integer", nullable: false),
                    ChallengeTotalCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseProgresses", x => new { x.CourseId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TrainingCourseProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseProgresses_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LocalFileId = table.Column<int>(type: "integer", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseResources_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseResources_Files_LocalFileId",
                        column: x => x.LocalFileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseResources_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseTeachers",
                columns: table => new
                {
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedById = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseTeachers", x => new { x.CourseId, x.TeacherId });
                    table.ForeignKey(
                        name: "FK_TrainingCourseTeachers_AspNetUsers_AssignedById",
                        column: x => x.AssignedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseTeachers_AspNetUsers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseTeachers_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingChapterProgresses",
                columns: table => new
                {
                    ChapterId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingChapterProgresses", x => new { x.ChapterId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TrainingChapterProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingChapterProgresses_TrainingCourseChapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "TrainingCourseChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseChapterChallenges",
                columns: table => new
                {
                    ChapterId = table.Column<int>(type: "integer", nullable: false),
                    ExerciseChallengeId = table.Column<int>(type: "integer", nullable: false),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCourseChapterChallenges", x => new { x.ChapterId, x.ExerciseChallengeId });
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterChallenges_TrainingCourseChallenges_Co~",
                        columns: x => new { x.CourseId, x.ExerciseChallengeId },
                        principalTable: "TrainingCourseChallenges",
                        principalColumns: new[] { "CourseId", "ExerciseChallengeId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseChapterChallenges_TrainingCourseChapters_Chap~",
                        column: x => x.ChapterId,
                        principalTable: "TrainingCourseChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCourseSubmissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    ChapterId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_TrainingCourseSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCourseSubmissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseSubmissions_ExerciseChallenges_ExerciseChalle~",
                        column: x => x.ExerciseChallengeId,
                        principalTable: "ExerciseChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCourseSubmissions_FlagContexts_FlagId",
                        column: x => x.FlagId,
                        principalTable: "FlagContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseSubmissions_TrainingCourseChapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "TrainingCourseChapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCourseSubmissions_TrainingCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "TrainingCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplates_TrainingCourseId",
                table: "ImageTemplates",
                column: "TrainingCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseChallenges_TrainingCourseId",
                table: "ExerciseChallenges",
                column: "TrainingCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingChapterProgresses_UserId_CompletedAt",
                table: "TrainingChapterProgresses",
                columns: new[] { "UserId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChallenges_CreatedById",
                table: "TrainingCourseChallenges",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChallenges_ExerciseChallengeId",
                table: "TrainingCourseChallenges",
                column: "ExerciseChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapterChallenges_CourseId_ExerciseChallengeId",
                table: "TrainingCourseChapterChallenges",
                columns: new[] { "CourseId", "ExerciseChallengeId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapters_CourseId_ParentId_Order",
                table: "TrainingCourseChapters",
                columns: new[] { "CourseId", "ParentId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapters_CreatedById",
                table: "TrainingCourseChapters",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapters_ParentId",
                table: "TrainingCourseChapters",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapters_UpdatedById",
                table: "TrainingCourseChapters",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseChapters_VideoFileId",
                table: "TrainingCourseChapters",
                column: "VideoFileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseEnrollments_ReviewedById",
                table: "TrainingCourseEnrollments",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseEnrollments_UserId_Status",
                table: "TrainingCourseEnrollments",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseProgresses_UpdatedAt",
                table: "TrainingCourseProgresses",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseProgresses_UserId_Status",
                table: "TrainingCourseProgresses",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseResources_CourseId_Order",
                table: "TrainingCourseResources",
                columns: new[] { "CourseId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseResources_CreatedById",
                table: "TrainingCourseResources",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseResources_LocalFileId",
                table: "TrainingCourseResources",
                column: "LocalFileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourses_CreatedById",
                table: "TrainingCourses",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourses_Status_UpdatedAt",
                table: "TrainingCourses",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourses_UpdatedById",
                table: "TrainingCourses",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseSubmissions_ChapterId",
                table: "TrainingCourseSubmissions",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseSubmissions_CourseId_ExerciseChallengeId_User~",
                table: "TrainingCourseSubmissions",
                columns: new[] { "CourseId", "ExerciseChallengeId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseSubmissions_ExerciseChallengeId",
                table: "TrainingCourseSubmissions",
                column: "ExerciseChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseSubmissions_FlagId",
                table: "TrainingCourseSubmissions",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseSubmissions_UserId_SubmittedAt",
                table: "TrainingCourseSubmissions",
                columns: new[] { "UserId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseTeachers_AssignedById",
                table: "TrainingCourseTeachers",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCourseTeachers_TeacherId",
                table: "TrainingCourseTeachers",
                column: "TeacherId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseChallenges_TrainingCourses_TrainingCourseId",
                table: "ExerciseChallenges",
                column: "TrainingCourseId",
                principalTable: "TrainingCourses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ImageTemplates_TrainingCourses_TrainingCourseId",
                table: "ImageTemplates",
                column: "TrainingCourseId",
                principalTable: "TrainingCourses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseChallenges_TrainingCourses_TrainingCourseId",
                table: "ExerciseChallenges");

            migrationBuilder.DropForeignKey(
                name: "FK_ImageTemplates_TrainingCourses_TrainingCourseId",
                table: "ImageTemplates");

            migrationBuilder.DropTable(
                name: "TrainingChapterProgresses");

            migrationBuilder.DropTable(
                name: "TrainingCourseChapterChallenges");

            migrationBuilder.DropTable(
                name: "TrainingCourseEnrollments");

            migrationBuilder.DropTable(
                name: "TrainingCourseProgresses");

            migrationBuilder.DropTable(
                name: "TrainingCourseResources");

            migrationBuilder.DropTable(
                name: "TrainingCourseSubmissions");

            migrationBuilder.DropTable(
                name: "TrainingCourseTeachers");

            migrationBuilder.DropTable(
                name: "TrainingCourseChallenges");

            migrationBuilder.DropTable(
                name: "TrainingCourseChapters");

            migrationBuilder.DropTable(
                name: "TrainingCourses");

            migrationBuilder.DropIndex(
                name: "IX_ImageTemplates_TrainingCourseId",
                table: "ImageTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseChallenges_TrainingCourseId",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "TrainingCourseId",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "TrainingCourseId",
                table: "ExerciseChallenges");
        }
    }
}
