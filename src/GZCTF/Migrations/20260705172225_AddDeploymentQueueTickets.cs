using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentQueueTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentQueueTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    DeploymentTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerTeamId = table.Column<int>(type: "integer", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    ChallengeId = table.Column<int>(type: "integer", nullable: true),
                    VmInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamLabRuntimeId = table.Column<int>(type: "integer", nullable: true),
                    DockerSlots = table.Column<int>(type: "integer", nullable: false),
                    VmSlots = table.Column<int>(type: "integer", nullable: false),
                    ActiveIdentity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentQueueTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentQueueTickets_DeploymentTargets_DeploymentTargetId",
                        column: x => x.DeploymentTargetId,
                        principalTable: "DeploymentTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DeploymentQueueTickets_WorkerNodes_TargetNodeId",
                        column: x => x.TargetNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueueTickets_ActiveIdentity",
                table: "DeploymentQueueTickets",
                column: "ActiveIdentity",
                unique: true,
                filter: "\"Status\" IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueueTickets_DeploymentTargetId",
                table: "DeploymentQueueTickets",
                column: "DeploymentTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueueTickets_Status_CreatedAt",
                table: "DeploymentQueueTickets",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueueTickets_TargetNodeId_Status",
                table: "DeploymentQueueTickets",
                columns: new[] { "TargetNodeId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentQueueTickets");
        }
    }
}
