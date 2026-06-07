using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdpModeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"AwdCheckerTasks\";");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"AwdFlags\";");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"AwdServiceInstances\";");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"AwdRounds\";");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"AwdServices\";");

            migrationBuilder.CreateTable(
                name: "AwdpRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttackPhaseStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PatchPhaseStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpRounds_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdpServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImageName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExposePort = table.Column<int>(type: "integer", nullable: false),
                    CheckerScript = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    CheckerEntrypoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpScript = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    ExpEntrypoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OriginalScore = table.Column<int>(type: "integer", nullable: false),
                    AttackPoints = table.Column<int>(type: "integer", nullable: false),
                    SlaPoints = table.Column<int>(type: "integer", nullable: false),
                    PatchPoints = table.Column<int>(type: "integer", nullable: false),
                    ServiceAbnormalPenalty = table.Column<int>(type: "integer", nullable: false),
                    MaxAttackPerRound = table.Column<int>(type: "integer", nullable: false),
                    AttackPhaseMinutes = table.Column<int>(type: "integer", nullable: false),
                    PatchPhaseMinutes = table.Column<int>(type: "integer", nullable: false),
                    TotalRounds = table.Column<int>(type: "integer", nullable: false),
                    MaxResetCount = table.Column<int>(type: "integer", nullable: false),
                    MaxRecoveryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpServices_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdpCheckerTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoundId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpCheckerTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpCheckerTasks_AwdpRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "AwdpRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpCheckerTasks_AwdpServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdpServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpCheckerTasks_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdpFlags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoundId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    FlagValue = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    IsSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedByTeamId = table.Column<int>(type: "integer", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpFlags_AspNetUsers_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AwdpFlags_AwdpRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "AwdpRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpFlags_AwdpServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdpServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpFlags_Teams_SubmittedByTeamId",
                        column: x => x.SubmittedByTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AwdpFlags_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdpPatchSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoundId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    PatchFileHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckerResult = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpResult = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FinalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpPatchSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpPatchSubmissions_AwdpRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "AwdpRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpPatchSubmissions_AwdpServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdpServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpPatchSubmissions_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdpRecoveryRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    RecoveryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpRecoveryRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpRecoveryRecords_AwdpServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdpServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpRecoveryRecords_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdpResetRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ResetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpResetRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpResetRecords_AwdpServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdpServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpResetRecords_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdpServiceInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ContainerId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    NetworkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpServiceInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdpServiceInstances_AwdpServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdpServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdpServiceInstances_Containers_ContainerId1",
                        column: x => x.ContainerId1,
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AwdpServiceInstances_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AwdpCheckerTasks_RoundId",
                table: "AwdpCheckerTasks",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpCheckerTasks_RoundId_ServiceId_TeamId",
                table: "AwdpCheckerTasks",
                columns: new[] { "RoundId", "ServiceId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwdpCheckerTasks_ServiceId",
                table: "AwdpCheckerTasks",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpCheckerTasks_TeamId",
                table: "AwdpCheckerTasks",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_FlagValue",
                table: "AwdpFlags",
                column: "FlagValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_RoundId",
                table: "AwdpFlags",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_RoundId_ServiceId_TeamId",
                table: "AwdpFlags",
                columns: new[] { "RoundId", "ServiceId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_ServiceId",
                table: "AwdpFlags",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_SubmittedByTeamId",
                table: "AwdpFlags",
                column: "SubmittedByTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_SubmittedByUserId",
                table: "AwdpFlags",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpFlags_TeamId",
                table: "AwdpFlags",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpPatchSubmissions_RoundId",
                table: "AwdpPatchSubmissions",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpPatchSubmissions_RoundId_ServiceId_TeamId_SubmittedAt",
                table: "AwdpPatchSubmissions",
                columns: new[] { "RoundId", "ServiceId", "TeamId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AwdpPatchSubmissions_ServiceId",
                table: "AwdpPatchSubmissions",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpPatchSubmissions_TeamId",
                table: "AwdpPatchSubmissions",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpRecoveryRecords_ServiceId",
                table: "AwdpRecoveryRecords",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpRecoveryRecords_ServiceId_TeamId_RecoveryAt",
                table: "AwdpRecoveryRecords",
                columns: new[] { "ServiceId", "TeamId", "RecoveryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AwdpRecoveryRecords_TeamId",
                table: "AwdpRecoveryRecords",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpResetRecords_ServiceId",
                table: "AwdpResetRecords",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpResetRecords_ServiceId_TeamId_ResetAt",
                table: "AwdpResetRecords",
                columns: new[] { "ServiceId", "TeamId", "ResetAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AwdpResetRecords_TeamId",
                table: "AwdpResetRecords",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpRounds_GameId",
                table: "AwdpRounds",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpRounds_GameId_RoundNumber",
                table: "AwdpRounds",
                columns: new[] { "GameId", "RoundNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwdpServiceInstances_ContainerId1",
                table: "AwdpServiceInstances",
                column: "ContainerId1");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpServiceInstances_ServiceId",
                table: "AwdpServiceInstances",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpServiceInstances_ServiceId_TeamId",
                table: "AwdpServiceInstances",
                columns: new[] { "ServiceId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwdpServiceInstances_TeamId",
                table: "AwdpServiceInstances",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpServices_GameId",
                table: "AwdpServices",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdpServices_GameId_Name",
                table: "AwdpServices",
                columns: new[] { "GameId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwdpCheckerTasks");

            migrationBuilder.DropTable(
                name: "AwdpFlags");

            migrationBuilder.DropTable(
                name: "AwdpPatchSubmissions");

            migrationBuilder.DropTable(
                name: "AwdpRecoveryRecords");

            migrationBuilder.DropTable(
                name: "AwdpResetRecords");

            migrationBuilder.DropTable(
                name: "AwdpServiceInstances");

            migrationBuilder.DropTable(
                name: "AwdpRounds");

            migrationBuilder.DropTable(
                name: "AwdpServices");

            migrationBuilder.CreateTable(
                name: "AwdServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ImageName = table.Column<string>(type: "text", nullable: false),
                    ExposePort = table.Column<int>(type: "integer", nullable: false),
                    CheckerScript = table.Column<string>(type: "text", nullable: true),
                    CheckerEntrypoint = table.Column<string>(type: "text", nullable: true),
                    OriginalScore = table.Column<int>(type: "integer", nullable: false),
                    AttackPoints = table.Column<int>(type: "integer", nullable: false),
                    SlaPoints = table.Column<int>(type: "integer", nullable: false),
                    MaxAttackPerRound = table.Column<int>(type: "integer", nullable: false),
                    RoundDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    TotalRounds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdServices_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdRounds_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdServiceInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ContainerId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    NetworkName = table.Column<string>(type: "text", nullable: false),
                    IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdServiceInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdServiceInstances_AwdServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdServiceInstances_Containers_ContainerUuid",
                        column: x => x.ContainerId1,
                        principalTable: "Containers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AwdServiceInstances_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdCheckerTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoundId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdCheckerTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdCheckerTasks_AwdRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "AwdRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdCheckerTasks_AwdServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdCheckerTasks_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AwdFlags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoundId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    FlagValue = table.Column<string>(type: "text", nullable: false),
                    IsSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwdFlags_AwdRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "AwdRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdFlags_AwdServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AwdServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AwdFlags_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AwdCheckerTasks_RoundId",
                table: "AwdCheckerTasks",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdCheckerTasks_ServiceId",
                table: "AwdCheckerTasks",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdCheckerTasks_TeamId",
                table: "AwdCheckerTasks",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdFlags_RoundId",
                table: "AwdFlags",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdFlags_ServiceId",
                table: "AwdFlags",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdFlags_TeamId",
                table: "AwdFlags",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdRounds_GameId",
                table: "AwdRounds",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdServiceInstances_ContainerId1",
                table: "AwdServiceInstances",
                column: "ContainerId1");

            migrationBuilder.CreateIndex(
                name: "IX_AwdServiceInstances_ServiceId",
                table: "AwdServiceInstances",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdServiceInstances_TeamId",
                table: "AwdServiceInstances",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AwdServices_GameId",
                table: "AwdServices",
                column: "GameId");
        }
    }
}
