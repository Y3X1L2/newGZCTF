using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814000000_AddTeamLabRuntimeExecutionModel")]
    public partial class AddTeamLabRuntimeExecutionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "TeamLabRuntimes"
                    ADD COLUMN IF NOT EXISTS "ExecutionModel" character varying(16) NOT NULL DEFAULT 'V2';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "TeamLabRuntimes"
                    DROP COLUMN IF EXISTS "ExecutionModel";
                """);
        }
    }
}