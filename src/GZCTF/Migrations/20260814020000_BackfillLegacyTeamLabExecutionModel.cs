using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814020000_BackfillLegacyTeamLabExecutionModel")]
    public partial class BackfillLegacyTeamLabExecutionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows that existed before the explicit execution model are legacy V1 deployments:
            // they never produced a V2 execution plan snapshot. Rows still queued keep their
            // declared model because no execution path has been applied yet.
            migrationBuilder.Sql("""
                UPDATE "TeamLabRuntimes" AS r
                SET "ExecutionModel" = 'V1'
                WHERE r."ExecutionModel" = 'V2'
                  AND r."Status" NOT IN (0, 1, 2)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "TeamLabExecutionPlanSnapshots" AS s
                      WHERE s."RuntimeId" = r."Id"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data correction is intentionally not reversed: restoring the broken V2 marker
            // would strand legacy runtimes in cleanup again.
        }
    }
}
