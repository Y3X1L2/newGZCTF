using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814010000_RemoveTeamLabRuntimeExecutionModelDefault")]
    public partial class RemoveTeamLabRuntimeExecutionModelDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "TeamLabRuntimes"
                    ALTER COLUMN "ExecutionModel" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "TeamLabRuntimes"
                    ALTER COLUMN "ExecutionModel" SET DEFAULT 'V2';
                """);
        }
    }
}
