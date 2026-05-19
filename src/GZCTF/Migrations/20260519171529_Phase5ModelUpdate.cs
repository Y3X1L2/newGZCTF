using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ModelUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseInstances_Containers_ContainerId",
                table: "ExerciseInstances");

            migrationBuilder.DropForeignKey(
                name: "FK_FlagContexts_GameChallenges_ChallengeId",
                table: "FlagContexts");

            migrationBuilder.DropForeignKey(
                name: "FK_GameInstances_Containers_ContainerId",
                table: "GameInstances");

            migrationBuilder.DropIndex(
                name: "IX_Containers_ExerciseInstanceId",
                table: "Containers");

            migrationBuilder.DropIndex(
                name: "IX_Containers_GameInstanceId",
                table: "Containers");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Submissions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ScenarioInstances",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<bool>(
                name: "ContainsMalware",
                table: "ImageTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "ImageTemplates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GameChallengeId",
                table: "FlagContexts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeploymentQueues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ImageTag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Dockerfile = table.Column<string>(type: "text", nullable: true),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExposedPorts = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EnvironmentVars = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockerImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GamePhases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CTFEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IREnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ScenarioEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SecurityPolicy = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePhases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamePhases_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
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
                name: "VmInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VmName = table.Column<string>(type: "text", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    SnapshotName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DestroyedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VmInstances_GameChallenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkerNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HostAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Capabilities = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    CpuLoad = table.Column<float>(type: "real", nullable: false),
                    MemoryLoad = table.Column<float>(type: "real", nullable: false),
                    CurrentContainers = table.Column<int>(type: "integer", nullable: false),
                    MaxContainers = table.Column<int>(type: "integer", nullable: false),
                    CurrentVms = table.Column<int>(type: "integer", nullable: false),
                    MaxVms = table.Column<int>(type: "integer", nullable: false),
                    UsedPorts = table.Column<int>(type: "integer", nullable: false),
                    TotalPorts = table.Column<int>(type: "integer", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Labels = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<byte>(type: "smallint", nullable: false),
                    Action = table.Column<byte>(type: "smallint", nullable: false),
                    Payload = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ResultPort = table.Column<int>(type: "integer", nullable: true),
                    ResultHost = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentTargets_WorkerNodes_TargetNodeId",
                        column: x => x.TargetNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_Status",
                table: "Submissions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlagContexts_GameChallengeId",
                table: "FlagContexts",
                column: "GameChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_ExerciseInstanceId",
                table: "Containers",
                column: "ExerciseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_GameInstanceId",
                table: "Containers",
                column: "GameInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_Status",
                table: "Containers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueues_Status",
                table: "DeploymentQueues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTargets_Status",
                table: "DeploymentTargets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentTargets_TargetNodeId",
                table: "DeploymentTargets",
                column: "TargetNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DockerImages_Status",
                table: "DockerImages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_GamePhases_GameId",
                table: "GamePhases",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_StageDependencies_RequiredStageId",
                table: "StageDependencies",
                column: "RequiredStageId");

            migrationBuilder.CreateIndex(
                name: "IX_VmInstances_ChallengeId",
                table: "VmInstances",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_VmInstances_Status",
                table: "VmInstances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerNodes_LastHeartbeat",
                table: "WorkerNodes",
                column: "LastHeartbeat");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerNodes_Status",
                table: "WorkerNodes",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseInstances_Containers_ContainerId",
                table: "ExerciseInstances",
                column: "ContainerId",
                principalTable: "Containers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FlagContexts_GameChallenges_ChallengeId",
                table: "FlagContexts",
                column: "ChallengeId",
                principalTable: "GameChallenges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FlagContexts_GameChallenges_GameChallengeId",
                table: "FlagContexts",
                column: "GameChallengeId",
                principalTable: "GameChallenges",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GameInstances_Containers_ContainerId",
                table: "GameInstances",
                column: "ContainerId",
                principalTable: "Containers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseInstances_Containers_ContainerId",
                table: "ExerciseInstances");

            migrationBuilder.DropForeignKey(
                name: "FK_FlagContexts_GameChallenges_ChallengeId",
                table: "FlagContexts");

            migrationBuilder.DropForeignKey(
                name: "FK_FlagContexts_GameChallenges_GameChallengeId",
                table: "FlagContexts");

            migrationBuilder.DropForeignKey(
                name: "FK_GameInstances_Containers_ContainerId",
                table: "GameInstances");

            migrationBuilder.DropTable(
                name: "DeploymentQueues");

            migrationBuilder.DropTable(
                name: "DeploymentTargets");

            migrationBuilder.DropTable(
                name: "DockerImages");

            migrationBuilder.DropTable(
                name: "GamePhases");

            migrationBuilder.DropTable(
                name: "StageDependencies");

            migrationBuilder.DropTable(
                name: "VmInstances");

            migrationBuilder.DropTable(
                name: "WorkerNodes");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_Status",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_FlagContexts_GameChallengeId",
                table: "FlagContexts");

            migrationBuilder.DropIndex(
                name: "IX_Containers_ExerciseInstanceId",
                table: "Containers");

            migrationBuilder.DropIndex(
                name: "IX_Containers_GameInstanceId",
                table: "Containers");

            migrationBuilder.DropIndex(
                name: "IX_Containers_Status",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ScenarioInstances");

            migrationBuilder.DropColumn(
                name: "ContainsMalware",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "GameChallengeId",
                table: "FlagContexts");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_ExerciseInstanceId",
                table: "Containers",
                column: "ExerciseInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Containers_GameInstanceId",
                table: "Containers",
                column: "GameInstanceId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseInstances_Containers_ContainerId",
                table: "ExerciseInstances",
                column: "ContainerId",
                principalTable: "Containers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FlagContexts_GameChallenges_ChallengeId",
                table: "FlagContexts",
                column: "ChallengeId",
                principalTable: "GameChallenges",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GameInstances_Containers_ContainerId",
                table: "GameInstances",
                column: "ContainerId",
                principalTable: "Containers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
