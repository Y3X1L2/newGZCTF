using Microsoft.EntityFrameworkCore.Migrations;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260811200000_RestoreTeamLabRuntimeScenarioBuild")]
public partial class RestoreTeamLabRuntimeScenarioBuild : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimes"
            ADD COLUMN IF NOT EXISTS "IsScenarioBuild" boolean NOT NULL DEFAULT FALSE;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimes"
            ALTER COLUMN "IsScenarioBuild" DROP DEFAULT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimes"
            DROP COLUMN IF EXISTS "IsScenarioBuild";
            """);
    }
}
