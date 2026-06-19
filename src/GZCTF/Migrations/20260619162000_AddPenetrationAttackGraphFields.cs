using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260619162000_AddPenetrationAttackGraphFields")]
    public partial class AddPenetrationAttackGraphFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCheckpoint",
                table: "PenetrationScoreItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PlayerAlias",
                table: "PenetrationNodes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerDescription",
                table: "PenetrationNodes",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCheckpoint",
                table: "PenetrationScoreItems");

            migrationBuilder.DropColumn(
                name: "PlayerAlias",
                table: "PenetrationNodes");

            migrationBuilder.DropColumn(
                name: "PlayerDescription",
                table: "PenetrationNodes");
        }
    }
}
