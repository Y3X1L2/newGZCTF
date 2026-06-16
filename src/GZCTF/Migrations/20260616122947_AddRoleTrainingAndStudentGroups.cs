using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleTrainingAndStudentGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentGroups_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TrainingDirections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingDirections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingDirections_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StudentGroupManagers",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleInGroup = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AddedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGroupManagers", x => new { x.GroupId, x.ManagerId });
                    table.ForeignKey(
                        name: "FK_StudentGroupManagers_AspNetUsers_AddedById",
                        column: x => x.AddedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StudentGroupManagers_AspNetUsers_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentGroupManagers_StudentGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentGroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedById = table.Column<Guid>(type: "uuid", nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGroupMembers", x => new { x.GroupId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_StudentGroupMembers_AspNetUsers_AddedById",
                        column: x => x.AddedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StudentGroupMembers_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentGroupMembers_StudentGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingModules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DirectionId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ArticleContent = table.Column<string>(type: "text", nullable: false),
                    ArticleContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CoverFileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EnvironmentTemplateId = table.Column<int>(type: "integer", nullable: true),
                    CompletionRule = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingModules_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingModules_AspNetUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingModules_ImageTemplates_EnvironmentTemplateId",
                        column: x => x.EnvironmentTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingModules_TrainingDirections_DirectionId",
                        column: x => x.DirectionId,
                        principalTable: "TrainingDirections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingModules_TrainingModules_ParentId",
                        column: x => x.ParentId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TheoryTrainingPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false),
                    BankName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    QuestionTypes = table.Column<string>(type: "text", nullable: true),
                    PassRate = table.Column<int>(type: "integer", nullable: false),
                    AllowRetake = table.Column<bool>(type: "boolean", nullable: false),
                    ShowCorrectAnswerAfterSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryTrainingPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingPlans_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingPlans_AspNetUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingPlans_TrainingModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingArticleProgresses",
                columns: table => new
                {
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadPercent = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingArticleProgresses", x => new { x.ModuleId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TrainingArticleProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingArticleProgresses_TrainingModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCtfSubmissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_TrainingCtfSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingCtfSubmissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCtfSubmissions_ExerciseChallenges_ExerciseChallenge~",
                        column: x => x.ExerciseChallengeId,
                        principalTable: "ExerciseChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingCtfSubmissions_FlagContexts_FlagId",
                        column: x => x.FlagId,
                        principalTable: "FlagContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingCtfSubmissions_TrainingModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingModuleChallenges",
                columns: table => new
                {
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    ExerciseChallengeId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingModuleChallenges", x => new { x.ModuleId, x.ExerciseChallengeId });
                    table.ForeignKey(
                        name: "FK_TrainingModuleChallenges_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingModuleChallenges_ExerciseChallenges_ExerciseChallen~",
                        column: x => x.ExerciseChallengeId,
                        principalTable: "ExerciseChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingModuleChallenges_TrainingModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingModuleProgresses",
                columns: table => new
                {
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChallengeSolvedCount = table.Column<int>(type: "integer", nullable: false),
                    ChallengeTotalCount = table.Column<int>(type: "integer", nullable: false),
                    TheoryBestScore = table.Column<int>(type: "integer", nullable: true),
                    TheoryBestPassRate = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingModuleProgresses", x => new { x.ModuleId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TrainingModuleProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingModuleProgresses_TrainingModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingModuleVisibilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: true),
                    VisibilityType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingModuleVisibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingModuleVisibilities_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TrainingModuleVisibilities_StudentGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingModuleVisibilities_TrainingModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TheoryTrainingPlanQuestions",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    SourceQuestionId = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryTrainingPlanQuestions", x => new { x.PlanId, x.SourceQuestionId });
                    table.ForeignKey(
                        name: "FK_TheoryTrainingPlanQuestions_TheoryQuestionBankItems_SourceQ~",
                        column: x => x.SourceQuestionId,
                        principalTable: "TheoryQuestionBankItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingPlanQuestions_TheoryTrainingPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TheoryTrainingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TheoryTrainingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    MaxScore = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryTrainingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingSessions_TheoryTrainingPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TheoryTrainingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingSessions_TrainingModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "TrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TheoryTrainingSessionQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    SourceQuestionId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: false),
                    AnswerIndexes = table.Column<string>(type: "text", nullable: false),
                    SelectedIndexes = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TheoryTrainingSessionQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TheoryTrainingSessionQuestions_TheoryTrainingSessions_Sessi~",
                        column: x => x.SessionId,
                        principalTable: "TheoryTrainingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroupManagers_AddedById",
                table: "StudentGroupManagers",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroupManagers_ManagerId",
                table: "StudentGroupManagers",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroupMembers_AddedById",
                table: "StudentGroupMembers",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroupMembers_StudentId",
                table: "StudentGroupMembers",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_CreatedById",
                table: "StudentGroups",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_Name",
                table: "StudentGroups",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingPlanQuestions_SourceQuestionId",
                table: "TheoryTrainingPlanQuestions",
                column: "SourceQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingPlans_CreatedById",
                table: "TheoryTrainingPlans",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingPlans_ModuleId",
                table: "TheoryTrainingPlans",
                column: "ModuleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingPlans_UpdatedById",
                table: "TheoryTrainingPlans",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingSessionQuestions_SessionId",
                table: "TheoryTrainingSessionQuestions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingSessions_ModuleId",
                table: "TheoryTrainingSessions",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingSessions_PlanId",
                table: "TheoryTrainingSessions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingSessions_UserId_ModuleId",
                table: "TheoryTrainingSessions",
                columns: new[] { "UserId", "ModuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_TheoryTrainingSessions_UserId_Status",
                table: "TheoryTrainingSessions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingArticleProgresses_UserId_CompletedAt",
                table: "TrainingArticleProgresses",
                columns: new[] { "UserId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCtfSubmissions_ExerciseChallengeId",
                table: "TrainingCtfSubmissions",
                column: "ExerciseChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCtfSubmissions_FlagId",
                table: "TrainingCtfSubmissions",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCtfSubmissions_ModuleId_ExerciseChallengeId_UserId",
                table: "TrainingCtfSubmissions",
                columns: new[] { "ModuleId", "ExerciseChallengeId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingCtfSubmissions_UserId_SubmittedAt",
                table: "TrainingCtfSubmissions",
                columns: new[] { "UserId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingDirections_CreatedById",
                table: "TrainingDirections",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingDirections_Type_Order",
                table: "TrainingDirections",
                columns: new[] { "Type", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModuleChallenges_CreatedById",
                table: "TrainingModuleChallenges",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModuleChallenges_ExerciseChallengeId",
                table: "TrainingModuleChallenges",
                column: "ExerciseChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModuleProgresses_UpdatedAt",
                table: "TrainingModuleProgresses",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModuleProgresses_UserId_Status",
                table: "TrainingModuleProgresses",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModules_CreatedById",
                table: "TrainingModules",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModules_DirectionId_ParentId_Order",
                table: "TrainingModules",
                columns: new[] { "DirectionId", "ParentId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModules_EnvironmentTemplateId",
                table: "TrainingModules",
                column: "EnvironmentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModules_ParentId",
                table: "TrainingModules",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModules_Type_IsPublished",
                table: "TrainingModules",
                columns: new[] { "Type", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModules_UpdatedById",
                table: "TrainingModules",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModuleVisibilities_CreatedById",
                table: "TrainingModuleVisibilities",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModuleVisibilities_GroupId",
                table: "TrainingModuleVisibilities",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingModuleVisibilities_ModuleId_VisibilityType_GroupId",
                table: "TrainingModuleVisibilities",
                columns: new[] { "ModuleId", "VisibilityType", "GroupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentGroupManagers");

            migrationBuilder.DropTable(
                name: "StudentGroupMembers");

            migrationBuilder.DropTable(
                name: "TheoryTrainingPlanQuestions");

            migrationBuilder.DropTable(
                name: "TheoryTrainingSessionQuestions");

            migrationBuilder.DropTable(
                name: "TrainingArticleProgresses");

            migrationBuilder.DropTable(
                name: "TrainingCtfSubmissions");

            migrationBuilder.DropTable(
                name: "TrainingModuleChallenges");

            migrationBuilder.DropTable(
                name: "TrainingModuleProgresses");

            migrationBuilder.DropTable(
                name: "TrainingModuleVisibilities");

            migrationBuilder.DropTable(
                name: "TheoryTrainingSessions");

            migrationBuilder.DropTable(
                name: "StudentGroups");

            migrationBuilder.DropTable(
                name: "TheoryTrainingPlans");

            migrationBuilder.DropTable(
                name: "TrainingModules");

            migrationBuilder.DropTable(
                name: "TrainingDirections");
        }
    }
}
