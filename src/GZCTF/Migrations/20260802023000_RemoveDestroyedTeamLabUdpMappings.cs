using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDestroyedTeamLabUdpMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM \"TeamLabPublicUdpMappings\" AS mapping
                USING \"TeamLabRuntimes\" AS runtime
                WHERE mapping.\"RuntimeId\" = runtime.\"Id\"
                  AND runtime.\"Status\" = 10;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
