using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedChallengeRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentQueues");

            migrationBuilder.DropTable(
                name: "DockerImages");

            migrationBuilder.DropTable(
                name: "IRCheckpoints");

            migrationBuilder.DropTable(
                name: "IRInstances");

            migrationBuilder.DropTable(
                name: "ScenarioInstances");

            migrationBuilder.DropTable(
                name: "ScoringRules");

            migrationBuilder.DropTable(
                name: "StageDependencies");

            migrationBuilder.DropTable(
                name: "TimeSlots");

            migrationBuilder.DropTable(
                name: "Stages");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseInstances_ContainerId",
                table: "ExerciseInstances");

            migrationBuilder.DropIndex(
                name: "IX_Containers_ExerciseInstanceId",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "ExerciseInstanceId",
                table: "Containers");

            migrationBuilder.AddColumn<int>(
                name: "FlagContextId",
                table: "Submissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlagId",
                table: "Submissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalArchiveName",
                table: "ImageTemplates",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PublicKey",
                table: "Games",
                type: "character varying(63)",
                maxLength: 63,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);

            migrationBuilder.AlterColumn<string>(
                name: "PrivateKey",
                table: "Games",
                type: "character varying(63)",
                maxLength: 63,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);

            migrationBuilder.AddColumn<byte>(
                name: "Environment",
                table: "GameChallenges",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "ImageTemplateId",
                table: "GameChallenges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "AnswerType",
                table: "FlagContexts",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentHash",
                table: "FlagContexts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomName",
                table: "FlagContexts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "FlagContexts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FixedScore",
                table: "FlagContexts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "FlagContexts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                table: "FlagContexts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "ScoreMode",
                table: "FlagContexts",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "FlagContextId",
                table: "FirstSolves",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlagId",
                table: "FirstSolves",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "Environment",
                table: "ExerciseChallenges",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "ImageTemplateId",
                table: "ExerciseChallenges",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_FlagContextId",
                table: "Submissions",
                column: "FlagContextId");

            migrationBuilder.CreateIndex(
                name: "IX_GameChallenges_ImageTemplateId",
                table: "GameChallenges",
                column: "ImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_FirstSolves_FlagContextId",
                table: "FirstSolves",
                column: "FlagContextId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseInstances_ContainerId",
                table: "ExerciseInstances",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseChallenges_ImageTemplateId",
                table: "ExerciseChallenges",
                column: "ImageTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseChallenges_ImageTemplates_ImageTemplateId",
                table: "ExerciseChallenges",
                column: "ImageTemplateId",
                principalTable: "ImageTemplates",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagContextId",
                table: "FirstSolves",
                column: "FlagContextId",
                principalTable: "FlagContexts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GameChallenges_ImageTemplates_ImageTemplateId",
                table: "GameChallenges",
                column: "ImageTemplateId",
                principalTable: "ImageTemplates",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Submissions_FlagContexts_FlagContextId",
                table: "Submissions",
                column: "FlagContextId",
                principalTable: "FlagContexts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseChallenges_ImageTemplates_ImageTemplateId",
                table: "ExerciseChallenges");

            migrationBuilder.DropForeignKey(
                name: "FK_FirstSolves_FlagContexts_FlagContextId",
                table: "FirstSolves");

            migrationBuilder.DropForeignKey(
                name: "FK_GameChallenges_ImageTemplates_ImageTemplateId",
                table: "GameChallenges");

            migrationBuilder.DropForeignKey(
                name: "FK_Submissions_FlagContexts_FlagContextId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_FlagContextId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_GameChallenges_ImageTemplateId",
                table: "GameChallenges");

            migrationBuilder.DropIndex(
                name: "IX_FirstSolves_FlagContextId",
                table: "FirstSolves");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseInstances_ContainerId",
                table: "ExerciseInstances");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseChallenges_ImageTemplateId",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "FlagContextId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "FlagId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "OriginalArchiveName",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "GameChallenges");

            migrationBuilder.DropColumn(
                name: "ImageTemplateId",
                table: "GameChallenges");

            migrationBuilder.DropColumn(
                name: "AnswerType",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "AttachmentHash",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "CustomName",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "FixedScore",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "OrderIndex",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "ScoreMode",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "FlagContextId",
                table: "FirstSolves");

            migrationBuilder.DropColumn(
                name: "FlagId",
                table: "FirstSolves");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "ImageTemplateId",
                table: "ExerciseChallenges");

            migrationBuilder.AlterColumn<string>(
                name: "PublicKey",
                table: "Games",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(63)",
                oldMaxLength: 63);

            migrationBuilder.AlterColumn<string>(
                name: "PrivateKey",
                table: "Games",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(63)",
                oldMaxLength: 63);

            migrationBuilder.AddColumn<int>(
                name: "ExerciseInstanceId",
                table: "Containers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeploymentQueues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    TargetNodeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentQueues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DockerImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Dockerfile = table.Column<string>(type: "text", nullable: true),
                    EnvironmentVars = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExposedPorts = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ImageTag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockerImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IRCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChallengeId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    VerificationConfig = table.Column<string>(type: "text", nullable: true),
                    VerificationType = table.Column<byte>(type: "smallint", nullable: false)
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
                    ExpectedAnswerHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ScoreDecay = table.Column<byte>(type: "smallint", nullable: false),
                    SubmissionType = table.Column<byte>(type: "smallint", nullable: false),
                    VerificationConfig = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    VerificationMode = table.Column<byte>(type: "smallint", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric", nullable: false)
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
                    EnvironmentImageIds = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FlagHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NetworkRules = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    PrerequisiteStageIds = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SkillDescription = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
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
                    CurrentParticipants = table.Column<int>(type: "integer", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MaxParticipants = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "StageDependencies",
                columns: table => new
                {
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    RequiredStageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageDependencies", x => new { x.StageId, x.RequiredStageId });
                    table.ForeignKey(
                        name: "FK_StageDependencies_Stages_RequiredStageId",
                        column: x => x.RequiredStageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageDependencies_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IRInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<int>(type: "integer", nullable: false),
                    TimeSlotId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessDetails = table.Column<string>(type: "text", nullable: true),
                    CheckpointResults = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EnvironmentStatus = table.Column<byte>(type: "smallint", nullable: false),
                    ResetCount = table.Column<int>(type: "integer", nullable: false),
                    ShellLog = table.Column<string>(type: "text", nullable: false)
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
                    TimeSlotId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentStageId = table.Column<int>(type: "integer", nullable: false),
                    StageStatuses = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    StageTimeline = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false)
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
                name: "IX_ExerciseInstances_ContainerId",
                table: "ExerciseInstances",
                column: "ContainerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Containers_ExerciseInstanceId",
                table: "Containers",
                column: "ExerciseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueues_Status",
                table: "DeploymentQueues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DockerImages_Status",
                table: "DockerImages",
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
                name: "IX_StageDependencies_RequiredStageId",
                table: "StageDependencies",
                column: "RequiredStageId");

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
        }
    }
}
