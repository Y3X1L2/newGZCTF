using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class CompleteTeamLabRuntimeReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceCursor",
                table: "TeamLabTrafficFlows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE "TeamLabTrafficFlows"
                SET "SourceCursor" = "Id";
                """);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKeyHash",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_TeamLabFlow_SourceCursor",
                table: "TeamLabTrafficFlows",
                columns: new[] { "RuntimeId", "Generation", "NetworkId", "SourceCursor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TeamLabCapture_Idempotency",
                table: "TeamLabTrafficCaptureJobs",
                columns: new[] { "RuntimeId", "Generation", "IdempotencyKeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TeamLabFlow_SourceCursor",
                table: "TeamLabTrafficFlows");

            migrationBuilder.DropIndex(
                name: "UX_TeamLabCapture_Idempotency",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "SourceCursor",
                table: "TeamLabTrafficFlows");

            migrationBuilder.DropColumn(
                name: "IdempotencyKeyHash",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "TeamLabTrafficCaptureJobs");
        }
    }
}
