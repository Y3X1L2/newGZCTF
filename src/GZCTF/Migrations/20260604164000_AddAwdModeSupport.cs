using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdModeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "GameType",
                table: "Games",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "OsType",
                table: "GameChallenges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsType",
                table: "ExerciseChallenges",
                type: "text",
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwdCheckerTasks");

            migrationBuilder.DropTable(
                name: "AwdFlags");

            migrationBuilder.DropTable(
                name: "AwdServiceInstances");

            migrationBuilder.DropTable(
                name: "AwdRounds");

            migrationBuilder.DropTable(
                name: "AwdServices");

            migrationBuilder.DropColumn(
                name: "GameType",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "OsType",
                table: "GameChallenges");

            migrationBuilder.DropColumn(
                name: "OsType",
                table: "ExerciseChallenges");
        }
    }
}
