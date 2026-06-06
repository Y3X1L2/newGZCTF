using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddScenarioAndIREntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "Submissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "Submissions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewComment",
                table: "Submissions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedById",
                table: "Submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Submissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "SubmissionType",
                table: "Submissions",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "ImageTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    ImageType = table.Column<byte>(type: "smallint", nullable: false),
                    RegistryUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RegistryAuth = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LocalFilePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IRCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChallengeId = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    VerificationType = table.Column<byte>(type: "smallint", nullable: false),
                    VerificationConfig = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IRCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IRCheckpoints_GameChallenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScoringRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChallengeId = table.Column<int>(type: "integer", nullable: false),
                    SubmissionType = table.Column<byte>(type: "smallint", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric", nullable: false),
                    VerificationMode = table.Column<byte>(type: "smallint", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ScoreDecay = table.Column<byte>(type: "smallint", nullable: false),
                    ExpectedAnswerHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VerificationConfig = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoringRules_GameChallenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScenarioId = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SkillDescription = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FlagHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NetworkRules = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PrerequisiteStageIds = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EnvironmentImageIds = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stages_GameChallenges_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScenarioId = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MaxParticipants = table.Column<int>(type: "integer", nullable: false),
                    CurrentParticipants = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeSlots_GameChallenges_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IRInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentStatus = table.Column<byte>(type: "smallint", nullable: false),
                    CheckpointResults = table.Column<string>(type: "text", nullable: false),
                    ShellLog = table.Column<string>(type: "text", nullable: false),
                    ResetCount = table.Column<int>(type: "integer", nullable: false),
                    AccessDetails = table.Column<string>(type: "text", nullable: true),
                    TimeSlotId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IRInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IRInstances_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IRInstances_GameChallenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IRInstances_TimeSlots_TimeSlotId",
                        column: x => x.TimeSlotId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScenarioInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStageId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    StageStatuses = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    StageTimeline = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeSlotId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScenarioInstances_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScenarioInstances_GameChallenges_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScenarioInstances_TimeSlots_TimeSlotId",
                        column: x => x.TimeSlotId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ReviewedById",
                table: "Submissions",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplates_Name",
                table: "ImageTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplates_Status",
                table: "ImageTemplates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IRCheckpoints_ChallengeId",
                table: "IRCheckpoints",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_IRCheckpoints_ChallengeId_OrderIndex",
                table: "IRCheckpoints",
                columns: new[] { "ChallengeId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IRInstances_ChallengeId",
                table: "IRInstances",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_IRInstances_ChallengeId_UserId",
                table: "IRInstances",
                columns: new[] { "ChallengeId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_IRInstances_EnvironmentStatus",
                table: "IRInstances",
                column: "EnvironmentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_IRInstances_TimeSlotId",
                table: "IRInstances",
                column: "TimeSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_IRInstances_UserId",
                table: "IRInstances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioInstances_ScenarioId",
                table: "ScenarioInstances",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioInstances_TimeSlotId",
                table: "ScenarioInstances",
                column: "TimeSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioInstances_UserId",
                table: "ScenarioInstances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoringRules_ChallengeId",
                table: "ScoringRules",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_ScenarioId",
                table: "Stages",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_ScenarioId_OrderIndex",
                table: "Stages",
                columns: new[] { "ScenarioId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeSlots_ScenarioId_StartTime",
                table: "TimeSlots",
                columns: new[] { "ScenarioId", "StartTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_Submissions_AspNetUsers_ReviewedById",
                table: "Submissions",
                column: "ReviewedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Submissions_AspNetUsers_ReviewedById",
                table: "Submissions");

            migrationBuilder.DropTable(
                name: "ImageTemplates");

            migrationBuilder.DropTable(
                name: "IRCheckpoints");

            migrationBuilder.DropTable(
                name: "IRInstances");

            migrationBuilder.DropTable(
                name: "ScenarioInstances");

            migrationBuilder.DropTable(
                name: "ScoringRules");

            migrationBuilder.DropTable(
                name: "Stages");

            migrationBuilder.DropTable(
                name: "TimeSlots");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_ReviewedById",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "Content",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "ReviewComment",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "SubmissionType",
                table: "Submissions");
        }
    }
}
