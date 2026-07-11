using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddImageImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageImportJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StagedPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestedTemplateKind = table.Column<byte>(type: "smallint", nullable: false),
                    RequestedOsType = table.Column<byte>(type: "smallint", nullable: false),
                    RequestedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageImportJobs", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_ImageImportJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImageImportJobs_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImageImportJobs_ImageTemplates_ImageTemplateId",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageImportJobs_CreatedById_RequestedName",
                table: "ImageImportJobs",
                columns: new[] { "CreatedById", "RequestedName" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageImportJobs_ImageTemplateId",
                table: "ImageImportJobs",
                column: "ImageTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageImportJobs");
        }
    }
}
