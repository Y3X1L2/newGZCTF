using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabRuntimeCreationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreationIdempotencyKey",
                table: "TeamLabRuntimes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_CreatedById_CreationIdempotencyKey",
                table: "TeamLabRuntimes",
                columns: new[] { "CreatedById", "CreationIdempotencyKey" },
                unique: true,
                filter: "\"CreationIdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_CreatedById_CreationIdempotencyKey",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "CreationIdempotencyKey",
                table: "TeamLabRuntimes");
        }
    }
}
