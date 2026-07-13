using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class HardenPhaseSevenRuntimeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RuntimeGeneration",
                table: "VmInstances",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "RuntimeNativeId",
                table: "VmInstances",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuntimeGeneration",
                table: "Containers",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuntimeGeneration",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "RuntimeNativeId",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "RuntimeGeneration",
                table: "Containers");
        }
    }
}
