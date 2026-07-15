using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class ContractPhaseSevenObservabilityAuditRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "DeploymentQueueTickets" ticket
                        WHERE ticket."Status" IN (0, 1, 2, 3)
                          AND NOT EXISTS (
                              SELECT 1 FROM "OperationalEvents" event
                              WHERE event."DeploymentTicketId" = ticket."Id"
                                AND event."EventCode" = 'runtime.snapshot.imported'
                          )
                    ) THEN
                        RAISE EXCEPTION 'Phase 7 contract rejected active deployment tickets without event baseline';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ImageDistributionRecords" record
                        WHERE (
                            record."Status" IN (0, 1, 4)
                            OR EXISTS (
                                SELECT 1 FROM "ImageDistributionReferences" reference
                                WHERE reference."DistributionRecordId" = record."Id"
                            )
                        )
                          AND NOT EXISTS (
                              SELECT 1 FROM "OperationalEvents" event
                              WHERE event."CorrelationId" = COALESCE(record."LastCorrelationId", record."Id")
                                AND event."ImageTemplateId" = record."ImageTemplateId"
                                AND event."WorkerNodeId" = record."WorkerNodeId"
                                AND event."EventCode" = 'image.snapshot.imported'
                          )
                    ) THEN
                        RAISE EXCEPTION 'Phase 7 contract rejected active image distributions without event baseline';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimes" runtime
                        WHERE runtime."Status" IN (0, 1, 2, 3, 4, 5, 7, 9)
                          AND NOT EXISTS (
                              SELECT 1 FROM "OperationalEvents" event
                              WHERE event."TeamLabRuntimeId" = runtime."Id"
                                AND event."EventCode" = 'teamlab.snapshot.imported'
                          )
                    ) THEN
                        RAISE EXCEPTION 'Phase 7 contract rejected active TeamLab runtimes without event baseline';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
