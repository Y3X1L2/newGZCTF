using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabRuntimeAssetFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Image",
                table: "TeamLabRuntimeAssets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "TeamLabRuntimeAssets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacAddress",
                table: "TeamLabRuntimeAssets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetworkKey",
                table: "TeamLabRuntimeAssets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceTemplateId",
                table: "TeamLabRuntimeAssets",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Image",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "MacAddress",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "NetworkKey",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "SourceTemplateId",
                table: "TeamLabRuntimeAssets");
        }
    }
}
