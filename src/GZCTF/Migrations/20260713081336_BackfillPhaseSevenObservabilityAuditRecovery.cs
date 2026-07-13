using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class BackfillPhaseSevenObservabilityAuditRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "OperationalEvents" (
                    "OccurredAt", "CorrelationId", "EventCode", "Severity", "Outcome",
                    "Message", "OwnerUserId", "OwnerTeamId", "GameId", "ChallengeId",
                    "WorkerNodeId", "DeploymentTicketId", "TeamLabRuntimeId", "VmInstanceId",
                    "SubjectType", "SubjectId", "SubjectDisplayName", "ResourceType",
                    "ResourceId", "ResourceDisplayName", "Retryable")
                SELECT
                    COALESCE(ticket."StartedAt", ticket."AssignedAt", ticket."CreatedAt"),
                    ticket."Id",
                    'runtime.snapshot.imported',
                    1,
                    7,
                    'Active runtime ticket imported into the Phase 7 event history.',
                    ticket."OwnerUserId",
                    ticket."OwnerTeamId",
                    ticket."GameId",
                    ticket."ChallengeId",
                    ticket."TargetNodeId",
                    ticket."Id",
                    ticket."TeamLabRuntimeId",
                    ticket."VmInstanceId",
                    ticket."SubjectType",
                    ticket."SubjectPublicId",
                    ticket."SubjectDisplayName",
                    CASE
                        WHEN ticket."VmInstanceId" IS NOT NULL THEN 'vm'
                        WHEN ticket."TeamLabRuntimeId" IS NOT NULL THEN 'teamlab-runtime'
                        ELSE 'runtime'
                    END,
                    COALESCE(ticket."VmInstanceId"::text, ticket."TeamLabRuntimeId"::text, ticket."SubjectPublicId"),
                    ticket."ResourceDisplayName",
                    false
                FROM "DeploymentQueueTickets" ticket
                WHERE ticket."Status" IN (0, 1, 2, 3)
                  AND NOT EXISTS (
                      SELECT 1 FROM "OperationalEvents" event
                      WHERE event."DeploymentTicketId" = ticket."Id"
                        AND event."EventCode" = 'runtime.snapshot.imported'
                  );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "OperationalEvents" (
                    "OccurredAt", "CorrelationId", "EventCode", "Severity", "Outcome",
                    "Message", "ImageTemplateId", "WorkerNodeId", "SubjectType", "SubjectId",
                    "SubjectDisplayName", "ResourceType", "ResourceId", "ResourceDisplayName",
                    "Retryable")
                SELECT
                    COALESCE(record."ProgressUpdatedAt", record."CreatedAt"),
                    COALESCE(record."LastCorrelationId", record."Id"),
                    'image.snapshot.imported',
                    1,
                    7,
                    'Active image distribution imported into the Phase 7 event history.',
                    record."ImageTemplateId",
                    record."WorkerNodeId",
                    'image-template',
                    record."ImageTemplateId"::text,
                    template."Name",
                    'worker-node',
                    record."WorkerNodeId"::text,
                    node."Name",
                    false
                FROM "ImageDistributionRecords" record
                JOIN "ImageTemplates" template ON template."Id" = record."ImageTemplateId"
                JOIN "WorkerNodes" node ON node."Id" = record."WorkerNodeId"
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
                  );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "OperationalEvents" (
                    "OccurredAt", "CorrelationId", "EventCode", "Severity", "Outcome",
                    "Message", "ActorUserId", "TeamLabRuntimeId", "SubjectType", "SubjectId",
                    "SubjectDisplayName", "ResourceType", "ResourceId", "ResourceDisplayName",
                    "Retryable")
                SELECT
                    COALESCE(runtime."UpdatedAt", runtime."CreatedAt"),
                    runtime."PublicId",
                    'teamlab.snapshot.imported',
                    1,
                    7,
                    'Active TeamLab runtime imported into the Phase 7 event history.',
                    runtime."CreatedById",
                    runtime."Id",
                    'teamlab-runtime',
                    runtime."PublicId"::text,
                    COALESCE(runtime."ExternalReference", runtime."PublicId"::text),
                    'teamlab-runtime',
                    runtime."Id"::text,
                    COALESCE(runtime."ExternalReference", runtime."PublicId"::text),
                    false
                FROM "TeamLabRuntimes" runtime
                WHERE runtime."Status" IN (0, 1, 2, 3, 4, 5, 7, 9)
                  AND NOT EXISTS (
                      SELECT 1 FROM "OperationalEvents" event
                      WHERE event."TeamLabRuntimeId" = runtime."Id"
                        AND event."EventCode" = 'teamlab.snapshot.imported'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "OperationalEvents"
                WHERE "EventCode" IN (
                    'runtime.snapshot.imported',
                    'image.snapshot.imported',
                    'teamlab.snapshot.imported'
                );
                """);
        }
    }
}
