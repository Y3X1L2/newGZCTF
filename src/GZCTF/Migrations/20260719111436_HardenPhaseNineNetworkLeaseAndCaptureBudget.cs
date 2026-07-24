using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class HardenPhaseNineNetworkLeaseAndCaptureBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MaxBytes",
                table: "TeamLabTrafficCaptureSegments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT s."Id",
                           j."MaxBytes",
                           COUNT(*) OVER (PARTITION BY s."CaptureJobId") AS segment_count,
                           ROW_NUMBER() OVER (
                               PARTITION BY s."CaptureJobId"
                               ORDER BY s."PublicId") AS segment_number
                    FROM "TeamLabTrafficCaptureSegments" s
                    JOIN "TeamLabTrafficCaptureJobs" j ON j."Id" = s."CaptureJobId"
                )
                UPDATE "TeamLabTrafficCaptureSegments" s
                SET "MaxBytes" = ranked."MaxBytes" / ranked.segment_count +
                    CASE
                        WHEN ranked.segment_number <= ranked."MaxBytes" % ranked.segment_count THEN 1
                        ELSE 0
                    END
                FROM ranked
                WHERE ranked."Id" = s."Id";

                CREATE UNIQUE INDEX "UX_TeamLabNetworkLeases_ActiveAllocatedCidr"
                ON "TeamLabNetworkLeases" ("AllocatedCidr")
                WHERE "ReleasedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "UX_TeamLabNetworkLeases_ActiveAllocatedCidr";
                """);

            migrationBuilder.DropColumn(
                name: "MaxBytes",
                table: "TeamLabTrafficCaptureSegments");
        }
    }
}
