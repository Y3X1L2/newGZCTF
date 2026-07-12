using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// Performs the short maintenance-window switch to the partitioned parents and removes superseded storage.
/// </summary>
public partial class ContractPhaseFourDatabaseGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL TIME ZONE 'UTC';

            LOCK TABLE "Participations", "ImageDistributionRecords", "Logs", "TeamLabTrafficFlows",
                       "TheoryQuestionBankItems", "TheoryQuestionTags", "TheoryQuestionTagBindings"
                IN ACCESS EXCLUSIVE MODE;

            DO $phase4$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "Participations"
                    GROUP BY "GameId", "TeamId"
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Phase 4 contract aborted: duplicate Participation(GameId, TeamId) rows exist';
                END IF;
            END
            $phase4$;

            INSERT INTO "TheoryQuestionTags" ("DisplayName", "NormalizedName", "CreatedAt")
            SELECT DISTINCT
                migrated.value,
                upper(migrated.value),
                CURRENT_TIMESTAMP
            FROM "TheoryQuestionBankItems" AS question
            CROSS JOIN LATERAL (
                SELECT regexp_replace(btrim(question."BankName"), '\s+', ' ', 'g') AS value
            ) AS bank
            CROSS JOIN LATERAL (
                SELECT CASE
                    WHEN length(bank.value) <= 59 THEN 'bank:' || bank.value
                    ELSE left('bank:' || bank.value, 55) || ':' ||
                         substr(encode(digest(bank.value, 'sha256'), 'hex'), 1, 8)
                END AS value
            ) AS migrated
            WHERE bank.value <> ''
            ON CONFLICT ("NormalizedName") DO NOTHING;

            INSERT INTO "TheoryQuestionTagBindings" ("QuestionId", "TagId")
            SELECT question."Id", tag."Id"
            FROM "TheoryQuestionBankItems" AS question
            CROSS JOIN LATERAL (
                SELECT regexp_replace(btrim(question."BankName"), '\s+', ' ', 'g') AS value
            ) AS bank
            CROSS JOIN LATERAL (
                SELECT CASE
                    WHEN length(bank.value) <= 59 THEN 'bank:' || bank.value
                    ELSE left('bank:' || bank.value, 55) || ':' ||
                         substr(encode(digest(bank.value, 'sha256'), 'hex'), 1, 8)
                END AS value
            ) AS migrated
            JOIN "TheoryQuestionTags" AS tag
              ON tag."NormalizedName" = upper(migrated.value)
            WHERE bank.value <> ''
            ON CONFLICT ("QuestionId", "TagId") DO NOTHING;

            SELECT setval(
                pg_get_serial_sequence('"TheoryQuestionTags"', 'Id'),
                COALESCE((SELECT max("Id") FROM "TheoryQuestionTags"), 1),
                EXISTS (SELECT 1 FROM "TheoryQuestionTags"));

            UPDATE "TeamLabTrafficFlows"
            SET
                "SourcePrefix" = CASE
                    WHEN pg_input_is_valid("SourceIp", 'inet') THEN
                        network(set_masklen("SourceIp"::inet,
                            CASE WHEN family("SourceIp"::inet) = 4 THEN 24 ELSE 64 END))::text
                    ELSE 'unknown'
                END,
                "DestinationPrefix" = CASE
                    WHEN pg_input_is_valid("DestinationIp", 'inet') THEN
                        network(set_masklen("DestinationIp"::inet,
                            CASE WHEN family("DestinationIp"::inet) = 4 THEN 24 ELSE 64 END))::text
                    ELSE 'unknown'
                END,
                "Fingerprint" = digest(
                    "RuntimeId"::text || '|' ||
                    "Generation"::text || '|' ||
                    COALESCE("NetworkId"::text, '') || '|' ||
                    btrim("SourceIp") || '|' ||
                    COALESCE("SourcePort"::text, '') || '|' ||
                    btrim("DestinationIp") || '|' ||
                    COALESCE("DestinationPort"::text, '') || '|' ||
                    upper(btrim("Protocol")) || '|' ||
                    (floor(extract(epoch FROM "CapturedAt") * 10000000)::bigint
                        + 621355968000000000)::text || '|' ||
                    "Bytes"::text || '|1',
                    'sha256')
            WHERE "SourcePrefix" IS NULL OR "DestinationPrefix" IS NULL OR "Fingerprint" IS NULL;

            INSERT INTO "Logs_phase4" SELECT * FROM "Logs"
                ON CONFLICT ("TimeUtc", "Id") DO NOTHING;
            INSERT INTO "TeamLabTrafficFlows_phase4" SELECT * FROM "TeamLabTrafficFlows"
                ON CONFLICT ("CapturedAt", "Id") DO NOTHING;

            DELETE FROM "ImageDistributionReferences";
            INSERT INTO "ImageDistributionReferences"
                ("Id", "DistributionRecordId", "Kind", "ResourceId", "CreatedAt")
            SELECT
                substr(encode(digest(
                    record."Id"::text || ':' || parsed.kind::text || ':' || parsed.resource_id::text,
                    'sha256'), 'hex'), 1, 32)::uuid,
                record."Id",
                parsed.kind,
                parsed.resource_id,
                record."CreatedAt"
            FROM "ImageDistributionRecords" AS record
            CROSS JOIN LATERAL jsonb_array_elements(
                COALESCE(NULLIF(record."References", ''), '[]')::jsonb) AS reference(value)
            CROSS JOIN LATERAL (
                SELECT
                    COALESCE(reference.value ->> 'Kind', reference.value ->> 'kind')::smallint AS kind,
                    COALESCE(reference.value ->> 'Id', reference.value ->> 'id')::integer AS resource_id
            ) AS parsed
            WHERE parsed.kind IN (0, 1) AND parsed.resource_id > 0
            ON CONFLICT ("DistributionRecordId", "Kind", "ResourceId") DO NOTHING;

            DO $phase4$
            DECLARE source_count bigint;
            DECLARE target_count bigint;
            DECLARE source_checksum numeric;
            DECLARE target_checksum numeric;
            BEGIN
                SELECT count(*), COALESCE(sum(hashtextextended(to_jsonb(source_row)::text, 0)), 0)
                    INTO source_count, source_checksum FROM "Logs" AS source_row;
                SELECT count(*), COALESCE(sum(hashtextextended(to_jsonb(target_row)::text, 0)), 0)
                    INTO target_count, target_checksum FROM "Logs_phase4" AS target_row;
                IF source_count <> target_count OR source_checksum <> target_checksum THEN
                    RAISE EXCEPTION 'Phase 4 log contract validation failed: source %/%, target %/%',
                        source_count, source_checksum, target_count, target_checksum;
                END IF;

                SELECT count(*), COALESCE(sum(hashtextextended(to_jsonb(source_row)::text, 0)), 0)
                    INTO source_count, source_checksum FROM "TeamLabTrafficFlows" AS source_row;
                SELECT count(*), COALESCE(sum(hashtextextended(to_jsonb(target_row)::text, 0)), 0)
                    INTO target_count, target_checksum FROM "TeamLabTrafficFlows_phase4" AS target_row;
                IF source_count <> target_count OR source_checksum <> target_checksum THEN
                    RAISE EXCEPTION 'Phase 4 flow contract validation failed: source %/%, target %/%',
                        source_count, source_checksum, target_count, target_checksum;
                END IF;

                IF EXISTS (SELECT 1 FROM "Logs_pdefault") OR
                   EXISTS (SELECT 1 FROM "TeamLabTrafficFlows_pdefault") THEN
                    RAISE EXCEPTION 'Phase 4 contract aborted: default partitions must be empty';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM "ImageDistributionRecords" AS record
                    WHERE (
                        SELECT count(DISTINCT (parsed.kind, parsed.resource_id))
                        FROM jsonb_array_elements(
                            COALESCE(NULLIF(record."References", ''), '[]')::jsonb) AS reference(value)
                        CROSS JOIN LATERAL (
                            SELECT
                                COALESCE(reference.value ->> 'Kind', reference.value ->> 'kind')::smallint AS kind,
                                COALESCE(reference.value ->> 'Id', reference.value ->> 'id')::integer AS resource_id
                        ) AS parsed
                        WHERE parsed.kind IN (0, 1) AND parsed.resource_id > 0
                    ) <> (
                        SELECT count(*)
                        FROM "ImageDistributionReferences" AS target
                        WHERE target."DistributionRecordId" = record."Id"
                    )
                ) THEN
                    RAISE EXCEPTION 'Phase 4 image reference checksum validation failed';
                END IF;
            END
            $phase4$;

            ALTER TABLE "TeamLabTrafficFlows_phase4"
                ALTER COLUMN "SourcePrefix" SET NOT NULL,
                ALTER COLUMN "DestinationPrefix" SET NOT NULL,
                ALTER COLUMN "Fingerprint" SET NOT NULL;

            ALTER TABLE "Logs" RENAME CONSTRAINT "PK_Logs" TO "PK_Logs_phase4_legacy";
            ALTER TABLE "TeamLabTrafficFlows" RENAME CONSTRAINT "PK_TeamLabTrafficFlows"
                TO "PK_TeamLabTrafficFlows_phase4_legacy";
            ALTER TABLE "Logs" RENAME TO "Logs_phase4_legacy";
            ALTER TABLE "TeamLabTrafficFlows" RENAME TO "TeamLabTrafficFlows_phase4_legacy";
            ALTER TABLE "Logs_phase4" RENAME TO "Logs";
            ALTER TABLE "TeamLabTrafficFlows_phase4" RENAME TO "TeamLabTrafficFlows";
            ALTER TABLE "Logs" RENAME CONSTRAINT "PK_Logs_phase4" TO "PK_Logs";
            ALTER TABLE "TeamLabTrafficFlows" RENAME CONSTRAINT "PK_TeamLabTrafficFlows_phase4"
                TO "PK_TeamLabTrafficFlows";

            ALTER TABLE "TeamLabTrafficFlows"
                ADD CONSTRAINT "FK_TeamLabTrafficFlows_TeamLabRuntimes_RuntimeId"
                    FOREIGN KEY ("RuntimeId") REFERENCES "TeamLabRuntimes" ("Id") ON DELETE CASCADE,
                ADD CONSTRAINT "FK_TeamLabTrafficFlows_TeamLabRuntimeShards_ShardId"
                    FOREIGN KEY ("ShardId") REFERENCES "TeamLabRuntimeShards" ("Id") ON DELETE SET NULL,
                ADD CONSTRAINT "FK_TeamLabTrafficFlows_TeamLabRuntimeNetworks_NetworkId"
                    FOREIGN KEY ("NetworkId") REFERENCES "TeamLabRuntimeNetworks" ("Id") ON DELETE SET NULL,
                ADD CONSTRAINT "FK_TeamLabTrafficFlows_WorkerNodes_WorkerNodeId"
                    FOREIGN KEY ("WorkerNodeId") REFERENCES "WorkerNodes" ("Id") ON DELETE SET NULL;

            DROP INDEX IF EXISTS "IX_TrainingCourseProgresses_UpdatedAt";
            DROP INDEX IF EXISTS "IX_TrainingCourseProgresses_UserId_Status";
            DROP INDEX IF EXISTS "IX_TrainingChapterProgresses_UserId_CompletedAt";
            CREATE INDEX IF NOT EXISTS "IX_TrainingCourseProgress_Course_Status_Updated_User"
                ON "TrainingCourseProgresses" ("CourseId", "Status", "UpdatedAt" DESC, "UserId");
            CREATE INDEX IF NOT EXISTS "IX_TrainingCourseProgress_User_Updated"
                ON "TrainingCourseProgresses" ("UserId", "UpdatedAt" DESC);
            CREATE INDEX IF NOT EXISTS "IX_TrainingChapterProgress_User_Updated"
                ON "TrainingChapterProgresses" ("UserId", "UpdatedAt" DESC);

            DROP INDEX IF EXISTS "IX_TheoryQuestionBankItems_Type_BankName";
            DROP INDEX IF EXISTS "IX_TheoryAnswerSheets_GameId";
            CREATE INDEX IF NOT EXISTS "IX_TheoryQuestions_Type_Updated_Id"
                ON "TheoryQuestionBankItems" ("Type", "UpdatedAt" DESC, "Id" DESC);
            CREATE INDEX IF NOT EXISTS "IX_TheoryQuestions_Title_Trgm"
                ON "TheoryQuestionBankItems" USING gin ("Title" gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS "IX_TheoryQuestions_Bank_Trgm"
                ON "TheoryQuestionBankItems" USING gin ("BankName" gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS "IX_TheoryAnswerSheets_Game_Status_Submitted_Id"
                ON "TheoryAnswerSheets" ("GameId", "Status", "SubmittedAt" DESC, "Id" DESC);

            DROP INDEX IF EXISTS "IX_Submissions_ChallengeId";
            DROP INDEX IF EXISTS "IX_Submissions_GameId";
            DROP INDEX IF EXISTS "IX_Submissions_ParticipationId";
            DROP INDEX IF EXISTS "IX_Submissions_Status";
            DROP INDEX IF EXISTS "IX_Submissions_TeamId_ChallengeId_GameId";
            CREATE INDEX IF NOT EXISTS "IX_Submissions_Challenge_Time_Id"
                ON "Submissions" ("ChallengeId", "SubmitTimeUtc" DESC, "Id" DESC);
            CREATE INDEX IF NOT EXISTS "IX_Submissions_Game_Time_Id"
                ON "Submissions" ("GameId", "SubmitTimeUtc" DESC, "Id" DESC);
            CREATE INDEX IF NOT EXISTS "IX_Submissions_Participation_Challenge"
                ON "Submissions" ("ParticipationId", "ChallengeId");
            CREATE INDEX IF NOT EXISTS "IX_Submissions_Team_Time_Id"
                ON "Submissions" ("TeamId", "SubmitTimeUtc" DESC, "Id" DESC);
            CREATE INDEX IF NOT EXISTS "IX_Submissions_Unchecked_Time_Id"
                ON "Submissions" ("Status", "SubmitTimeUtc" DESC, "Id" DESC)
                WHERE "Status" = 'FlagSubmitted';

            DROP INDEX IF EXISTS "IX_Participations_GameId";
            DROP INDEX IF EXISTS "IX_Participations_TeamId_GameId";
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_Participations_Game_Team"
                ON "Participations" ("GameId", "TeamId");
            CREATE INDEX IF NOT EXISTS "IX_Participations_Game_Status_Division_Team"
                ON "Participations" ("GameId", "Status", "DivisionId", "TeamId");

            DROP INDEX IF EXISTS "IX_ImageDistributionRecords_ImageTemplateId_WorkerNodeId";
            DROP INDEX IF EXISTS "IX_ImageDistributionRecords_Status";
            DROP INDEX IF EXISTS "IX_ImageDistributionRecords_WorkerNodeId";
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_ImageDistributionRecords_Template_Node"
                ON "ImageDistributionRecords" ("ImageTemplateId", "WorkerNodeId");
            CREATE INDEX IF NOT EXISTS "IX_ImageDistributionRecords_Node_Status_Checked"
                ON "ImageDistributionRecords" ("WorkerNodeId", "Status", "LastCheckedAt");

            DROP INDEX IF EXISTS "IX_DeploymentQueueTickets_ActiveIdentity";
            DROP INDEX IF EXISTS "IX_DeploymentQueueTickets_Status_CreatedAt";
            DROP INDEX IF EXISTS "IX_DeploymentQueueTickets_TargetNodeId_Status";
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_DeploymentQueueTickets_ActiveIdentity"
                ON "DeploymentQueueTickets" ("ActiveIdentity") WHERE "Status" IN (0, 1, 2);
            CREATE INDEX IF NOT EXISTS "IX_DeploymentQueueTickets_Status_Created_Id"
                ON "DeploymentQueueTickets" ("Status", "CreatedAt", "Id");
            CREATE INDEX IF NOT EXISTS "IX_DeploymentQueueTickets_Node_Status_Created_Id"
                ON "DeploymentQueueTickets" ("TargetNodeId", "Status", "CreatedAt", "Id");
            CREATE INDEX IF NOT EXISTS "IX_DeploymentQueueTickets_Terminal_Completed_Id"
                ON "DeploymentQueueTickets" ("Status", "CompletedAt" DESC, "Id" DESC)
                WHERE "Status" IN (3, 4, 5);

            DROP INDEX IF EXISTS "IX_AwdpRounds_GameId";
            DROP INDEX IF EXISTS "IX_AwdpRounds_GameId_RoundNumber";
            DROP INDEX IF EXISTS "IX_AwdpCheckerTasks_RoundId";
            DROP INDEX IF EXISTS "IX_AwdpCheckerTasks_RoundId_ServiceId_TeamId";
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_AwdpRounds_Game_Round"
                ON "AwdpRounds" ("GameId", "RoundNumber");
            CREATE INDEX IF NOT EXISTS "IX_AwdpRounds_Game_Status_Round"
                ON "AwdpRounds" ("GameId", "Status", "RoundNumber" DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_AwdpCheckerTasks_Round_Service_Team"
                ON "AwdpCheckerTasks" ("RoundId", "ServiceId", "TeamId");
            CREATE INDEX IF NOT EXISTS "IX_AwdpCheckerTasks_Status_Executed_Id"
                ON "AwdpCheckerTasks" ("Status", "ExecutedAt", "Id");

            ALTER TABLE "ImageDistributionRecords"
                DROP COLUMN "ReferenceCount",
                DROP COLUMN "References";

            SELECT setval(
                pg_get_serial_sequence('"Logs"', 'Id'),
                COALESCE((SELECT max("Id") FROM "Logs"), 1),
                EXISTS (SELECT 1 FROM "Logs"));
            SELECT setval(
                pg_get_serial_sequence('"TeamLabTrafficFlows"', 'Id'),
                COALESCE((SELECT max("Id") FROM "TeamLabTrafficFlows"), 1),
                EXISTS (SELECT 1 FROM "TeamLabTrafficFlows"));

            DROP TABLE "Logs_phase4_legacy";
            DROP TABLE "TeamLabTrafficFlows_phase4_legacy";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Phase 4 database governance migrations are forward-only. Restore the pre-migration backup or use PITR.");
}
