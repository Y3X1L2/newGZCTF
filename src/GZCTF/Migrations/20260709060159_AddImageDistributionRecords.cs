using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddImageDistributionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageDistributionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImageType = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ReferenceCount = table.Column<int>(type: "integer", nullable: false),
                    References = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageDistributionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageDistributionRecords_ImageTemplates_ImageTemplateId",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImageDistributionRecords_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageDistributionRecords_ImageTemplateId_WorkerNodeId",
                table: "ImageDistributionRecords",
                columns: new[] { "ImageTemplateId", "WorkerNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageDistributionRecords_Status",
                table: "ImageDistributionRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImageDistributionRecords_WorkerNodeId",
                table: "ImageDistributionRecords",
                column: "WorkerNodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageDistributionRecords");
        }
    }
}
