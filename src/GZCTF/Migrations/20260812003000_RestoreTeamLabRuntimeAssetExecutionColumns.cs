using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812003000_RestoreTeamLabRuntimeAssetExecutionColumns")]
public partial class RestoreTeamLabRuntimeAssetExecutionColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimeAssets"
            ADD COLUMN IF NOT EXISTS "BootstrapDigest" character varying(128);
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimeAssets"
            ADD COLUMN IF NOT EXISTS "Stateless" boolean NOT NULL DEFAULT FALSE;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimeAssets"
            ALTER COLUMN "Stateless" DROP DEFAULT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimeAssets"
            DROP COLUMN IF EXISTS "BootstrapDigest";
            """);
        migrationBuilder.Sql("""
            ALTER TABLE "TeamLabRuntimeAssets"
            DROP COLUMN IF EXISTS "Stateless";
            """);
    }
}
