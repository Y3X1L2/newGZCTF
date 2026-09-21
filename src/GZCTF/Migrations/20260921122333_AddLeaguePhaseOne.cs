using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaguePhaseOne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeagueMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    TopologyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitialCoins = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationVersion = table.Column<int>(type: "integer", nullable: true),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationState = table.Column<int>(type: "integer", nullable: false),
                    Failure = table.Column<int>(type: "integer", nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CleanupOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CleanupState = table.Column<int>(type: "integer", nullable: false),
                    CleanupFailure = table.Column<int>(type: "integer", nullable: false),
                    CleanupRetryable = table.Column<bool>(type: "boolean", nullable: false),
                    CleanupAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    WinnerTeamId = table.Column<int>(type: "integer", nullable: true),
                    EndReason = table.Column<int>(type: "integer", nullable: true),
                    AbortReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WinningSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EndedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMatches", x => x.Id);
                    table.CheckConstraint("CK_LeagueMatches_Coins", "\"InitialCoins\" >= 0");
                    table.CheckConstraint("CK_LeagueMatches_Result", "(\"State\" = 5 AND \"EndedAt\" IS NOT NULL AND \"EndReason\" IS NOT NULL AND ((\"EndReason\" = 0 AND \"WinnerTeamId\" IS NOT NULL AND \"WinningSubmissionId\" IS NOT NULL) OR (\"EndReason\" = 1 AND \"WinnerTeamId\" IS NULL AND \"WinningSubmissionId\" IS NULL))) OR (\"State\" <> 5 AND \"EndedAt\" IS NULL AND \"EndReason\" IS NULL AND \"WinnerTeamId\" IS NULL AND \"WinningSubmissionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_LeagueMatches_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueMatches_TeamLabTopologyReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "TeamLabTopologyReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeagueRegistrations",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    TeamName = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegisteredById = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Selected = table.Column<bool>(type: "boolean", nullable: false),
                    Seat = table.Column<int>(type: "integer", nullable: true),
                    MemberIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    PreparationState = table.Column<int>(type: "integer", nullable: false),
                    RuntimeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Generation = table.Column<int>(type: "integer", nullable: true),
                    EnvironmentReady = table.Column<bool>(type: "boolean", nullable: false),
                    FlagInjected = table.Column<bool>(type: "boolean", nullable: false),
                    EntryPrepared = table.Column<bool>(type: "boolean", nullable: false),
                    AccessClosed = table.Column<bool>(type: "boolean", nullable: false),
                    Failure = table.Column<int>(type: "integer", nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueRegistrations", x => new { x.MatchId, x.TeamId });
                    table.CheckConstraint("CK_LeagueRegistrations_Seat", "(\"Selected\" AND \"Seat\" IS NOT NULL AND \"Seat\" IN (1, 2)) OR (NOT \"Selected\" AND \"Seat\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_LeagueRegistrations_LeagueMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "LeagueMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueRegistrations_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_CreatedById",
                table: "LeagueMatches",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_ReleaseId",
                table: "LeagueMatches",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_State_NextAttemptAt",
                table: "LeagueMatches",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_WinningSubmissionId",
                table: "LeagueMatches",
                column: "WinningSubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueRegistrations_MatchId_Seat",
                table: "LeagueRegistrations",
                columns: new[] { "MatchId", "Seat" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueRegistrations_TeamId",
                table: "LeagueRegistrations",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueRegistrations");

            migrationBuilder.DropTable(
                name: "LeagueMatches");
        }
    }
}
