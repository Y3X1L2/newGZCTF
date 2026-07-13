using Microsoft.EntityFrameworkCore.Migrations;

using System;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class ContractPhaseSixRuntimeSchedulingConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "DeploymentQueueTickets"
                        WHERE "SubjectConcurrencyKey" = '' OR "Generation" < 1 OR "Operation" NOT IN (1,2,3,4,5)
                    ) THEN
                        RAISE EXCEPTION 'Phase 6 contract aborted: deployment ticket backfill is incomplete';
                    END IF;
                    IF EXISTS (
                        SELECT "SubjectConcurrencyKey"
                        FROM "DeploymentQueueTickets"
                        WHERE "Status" IN (0,1,2,3)
                        GROUP BY "SubjectConcurrencyKey"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Phase 6 contract aborted: duplicate active runtime subjects exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                "FK_DeploymentQueueTickets_DeploymentTargets_DeploymentTargetId",
                "DeploymentQueueTickets");
            migrationBuilder.DropIndex(
                "IX_DeploymentQueueTickets_DeploymentTargetId",
                "DeploymentQueueTickets");
            migrationBuilder.DropTable("DeploymentTargets");
            migrationBuilder.DropColumn("DeploymentTargetId", "DeploymentQueueTickets");

            migrationBuilder.DropColumn("ReservedContainers", "WorkerNodes");
            migrationBuilder.DropColumn("ReservedVms", "WorkerNodes");
            migrationBuilder.DropColumn("TeamLabProtocolVersion", "WorkerNodes");
            migrationBuilder.DropColumn("TeamLabAgentVersion", "WorkerNodes");
            migrationBuilder.DropColumn("TeamLabCapabilitiesJson", "WorkerNodes");

            migrationBuilder.DropIndex(
                "IX_DeploymentQueueTickets_Status_Created_Id",
                "DeploymentQueueTickets");
            migrationBuilder.DropIndex(
                "IX_DeploymentQueueTickets_Terminal_Completed_Id",
                "DeploymentQueueTickets");
            migrationBuilder.DropIndex(
                "UX_DeploymentQueueTickets_ActiveIdentity",
                "DeploymentQueueTickets");

            migrationBuilder.CreateIndex(
                "IX_DeploymentQueueTickets_Status_NotBefore_Created_Id",
                "DeploymentQueueTickets",
                new[] { "Status", "NotBeforeAt", "CreatedAt", "Id" });
            migrationBuilder.CreateIndex(
                "IX_DeploymentQueueTickets_Terminal_Completed_Id",
                "DeploymentQueueTickets",
                new[] { "Status", "CompletedAt", "Id" },
                descending: new[] { false, true, true },
                filter: "\"Status\" IN (4, 5, 6)");
            migrationBuilder.CreateIndex(
                "UX_DeploymentQueueTickets_ActiveIdentity",
                "DeploymentQueueTickets",
                "ActiveIdentity",
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 3)");
            migrationBuilder.CreateIndex(
                "UX_DeploymentQueueTickets_SubjectConcurrencyKey",
                "DeploymentQueueTickets",
                "SubjectConcurrencyKey",
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Phase 6 contract removes legacy runtime tables; restore the pre-contract backup instead.");
        }
    }
}
