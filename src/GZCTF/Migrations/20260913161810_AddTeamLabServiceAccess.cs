using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabServiceAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLabServiceAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    RuntimeAssetId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Protocol = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    InternalPort = table.Column<int>(type: "integer", nullable: false),
                    PublicPort = table.Column<int>(type: "integer", nullable: false),
                    PortLeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabServiceAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabServiceAccesses_TeamLabRuntimeAssets_RuntimeAssetId",
                        column: x => x.RuntimeAssetId,
                        principalTable: "TeamLabRuntimeAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabServiceAccesses_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabServiceAccesses_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabServiceAccesses_PublicId",
                table: "TeamLabServiceAccesses",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabServiceAccesses_PublicPort",
                table: "TeamLabServiceAccesses",
                column: "PublicPort",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabServiceAccesses_RuntimeAssetId",
                table: "TeamLabServiceAccesses",
                column: "RuntimeAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabServiceAccesses_RuntimeId_Generation_RuntimeAssetId",
                table: "TeamLabServiceAccesses",
                columns: new[] { "RuntimeId", "Generation", "RuntimeAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabServiceAccesses_WorkerNodeId",
                table: "TeamLabServiceAccesses",
                column: "WorkerNodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabServiceAccesses");
        }
    }
}
