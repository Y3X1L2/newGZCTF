using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class HardenTeamLabRemoteSessionCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRemoteSessions"
                        WHERE "Status" IN (1, 2, 3, 4)
                        GROUP BY "RequestedByUserId", "RuntimeAssetId", "Protocol"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot enforce TeamLab remote-session uniqueness while duplicate active sessions remain. Let cleanup finish or close the duplicate sessions first.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRemoteSessions_RequestedByUserId",
                table: "TeamLabRemoteSessions");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_RequestedByUserId_RuntimeAssetId_Prot~",
                table: "TeamLabRemoteSessions",
                columns: new[] { "RequestedByUserId", "RuntimeAssetId", "Protocol" },
                unique: true,
                filter: "\"Status\" IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabRemoteSessions_RequestedByUserId_RuntimeAssetId_Prot~",
                table: "TeamLabRemoteSessions");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRemoteSessions_RequestedByUserId",
                table: "TeamLabRemoteSessions",
                column: "RequestedByUserId");
        }
    }
}
