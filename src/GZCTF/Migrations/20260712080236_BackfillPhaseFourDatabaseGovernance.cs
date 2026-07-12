using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// Backfills relational facts and shadow partition parents while the legacy tables remain canonical.
/// </summary>
public partial class BackfillPhaseFourDatabaseGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL TIME ZONE 'UTC';

            DO $phase4$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "ImageDistributionRecords"
                    WHERE NOT pg_input_is_valid(COALESCE(NULLIF("References", ''), '[]'), 'jsonb')
                ) THEN
                    RAISE EXCEPTION 'Phase 4 backfill aborted: ImageDistributionRecords.References contains invalid JSON';
                END IF;
            END
            $phase4$;

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

            DO $phase4$
            DECLARE
                boundary timestamp with time zone;
                final_boundary timestamp with time zone;
                partition_name text;
            BEGIN
                boundary := date_trunc('month', COALESCE(
                    (SELECT min("TimeUtc") FROM "Logs"), CURRENT_TIMESTAMP));
                final_boundary := greatest(
                    date_trunc('month', COALESCE((SELECT max("TimeUtc") FROM "Logs"), CURRENT_TIMESTAMP)) + interval '1 month',
                    date_trunc('month', CURRENT_TIMESTAMP) + interval '3 months');
                WHILE boundary < final_boundary LOOP
                    partition_name := 'Logs_p' || to_char(boundary AT TIME ZONE 'UTC', 'YYYYMM');
                    EXECUTE format(
                        'CREATE TABLE IF NOT EXISTS %I PARTITION OF "Logs_phase4" FOR VALUES FROM (%L) TO (%L)',
                        partition_name, boundary, boundary + interval '1 month');
                    boundary := boundary + interval '1 month';
                END LOOP;

                boundary := date_trunc('day', COALESCE(
                    (SELECT min("CapturedAt") FROM "TeamLabTrafficFlows"), CURRENT_TIMESTAMP));
                final_boundary := greatest(
                    date_trunc('day', COALESCE((SELECT max("CapturedAt") FROM "TeamLabTrafficFlows"), CURRENT_TIMESTAMP)) + interval '1 day',
                    date_trunc('day', CURRENT_TIMESTAMP) + interval '3 days');
                WHILE boundary < final_boundary LOOP
                    partition_name := 'TeamLabTrafficFlows_p' || to_char(boundary AT TIME ZONE 'UTC', 'YYYYMMDD');
                    EXECUTE format(
                        'CREATE TABLE IF NOT EXISTS %I PARTITION OF "TeamLabTrafficFlows_phase4" FOR VALUES FROM (%L) TO (%L)',
                        partition_name, boundary, boundary + interval '1 day');
                    boundary := boundary + interval '1 day';
                END LOOP;
            END
            $phase4$;

            CREATE TABLE "Logs_pdefault" PARTITION OF "Logs_phase4" DEFAULT;
            CREATE TABLE "TeamLabTrafficFlows_pdefault" PARTITION OF "TeamLabTrafficFlows_phase4" DEFAULT;

            INSERT INTO "Logs_phase4" SELECT * FROM "Logs" ORDER BY "TimeUtc", "Id";
            INSERT INTO "TeamLabTrafficFlows_phase4"
                SELECT * FROM "TeamLabTrafficFlows" ORDER BY "CapturedAt", "Id";

            ALTER TABLE "Logs_phase4"
                ADD CONSTRAINT "PK_Logs_phase4" PRIMARY KEY ("TimeUtc", "Id");
            ALTER TABLE "TeamLabTrafficFlows_phase4"
                ADD CONSTRAINT "PK_TeamLabTrafficFlows_phase4" PRIMARY KEY ("CapturedAt", "Id");

            CREATE INDEX "IX_Logs_Time_Id"
                ON "Logs_phase4" ("TimeUtc" DESC, "Id" DESC);
            CREATE INDEX "IX_Logs_Level_Time_Id"
                ON "Logs_phase4" ("Level", "TimeUtc" DESC, "Id" DESC);
            CREATE INDEX "IX_TeamLabFlows_Runtime_Generation_Time_Id"
                ON "TeamLabTrafficFlows_phase4" ("RuntimeId", "Generation", "CapturedAt" DESC, "Id" DESC);
            CREATE INDEX "IX_TeamLabFlows_Shard_Time_Id"
                ON "TeamLabTrafficFlows_phase4" ("ShardId", "CapturedAt" DESC, "Id" DESC);
            CREATE UNIQUE INDEX "UX_TeamLabFlows_Time_Runtime_Generation_Fingerprint"
                ON "TeamLabTrafficFlows_phase4" ("CapturedAt", "RuntimeId", "Generation", "Fingerprint");

            DO $phase4$
            DECLARE source_count bigint;
            DECLARE target_count bigint;
            BEGIN
                SELECT count(*) INTO source_count FROM "Logs";
                SELECT count(*) INTO target_count FROM "Logs_phase4";
                IF source_count <> target_count THEN
                    RAISE EXCEPTION 'Phase 4 log backfill count mismatch: source %, target %', source_count, target_count;
                END IF;

                SELECT count(*) INTO source_count FROM "TeamLabTrafficFlows";
                SELECT count(*) INTO target_count FROM "TeamLabTrafficFlows_phase4";
                IF source_count <> target_count THEN
                    RAISE EXCEPTION 'Phase 4 flow backfill count mismatch: source %, target %', source_count, target_count;
                END IF;
            END
            $phase4$;

            SELECT setval(
                pg_get_serial_sequence('"Logs_phase4"', 'Id'),
                COALESCE((SELECT max("Id") FROM "Logs_phase4"), 1),
                EXISTS (SELECT 1 FROM "Logs_phase4"));
            SELECT setval(
                pg_get_serial_sequence('"TeamLabTrafficFlows_phase4"', 'Id'),
                COALESCE((SELECT max("Id") FROM "TeamLabTrafficFlows_phase4"), 1),
                EXISTS (SELECT 1 FROM "TeamLabTrafficFlows_phase4"));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Phase 4 database governance migrations are forward-only. Restore the pre-migration backup or use PITR.");
}
