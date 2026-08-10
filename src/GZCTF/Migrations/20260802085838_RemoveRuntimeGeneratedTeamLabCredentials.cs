using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRuntimeGeneratedTeamLabCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabRuntimeRemoteCredentials");

            migrationBuilder.DropColumn(
                name: "CredentialMode",
                table: "ImageTemplateRemoteAccesses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "CredentialMode",
                table: "ImageTemplateRemoteAccesses",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeRemoteCredentials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeAssetId = table.Column<int>(type: "integer", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<byte>(type: "smallint", nullable: false),
                    ProtectedSecret = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    Protocol = table.Column<byte>(type: "smallint", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeRemoteCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeRemoteCredentials_TeamLabRuntimeAssets_Runtim~",
                        column: x => x.RuntimeAssetId,
                        principalTable: "TeamLabRuntimeAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeRemoteCredentials_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeRemoteCredentials_RuntimeAssetId",
                table: "TeamLabRuntimeRemoteCredentials",
                column: "RuntimeAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeRemoteCredentials_RuntimeId_Generation_Runtim~",
                table: "TeamLabRuntimeRemoteCredentials",
                columns: new[] { "RuntimeId", "Generation", "RuntimeAssetId", "Protocol" },
                unique: true);
        }
    }
}
