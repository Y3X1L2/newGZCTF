using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemLogResourceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Logs_Resource_Time_Id",
                table: "Logs",
                columns: new[] { "ResourceType", "ResourceId", "TimeUtc", "Id" },
                descending: new[] { false, false, true, true },
                filter: "\"ResourceType\" IS NOT NULL AND \"ResourceId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Logs_Resource_Time_Id",
                table: "Logs");
        }
    }
}
