using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddVmImageSourceUploadOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VmImageBuildSourceUploadJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    StagedPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    OsFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Architecture = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmImageBuildSourceUploadJobs", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_VmImageBuildSourceUploadJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VmImageBuildSourceUploadJobs_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VmImageBuildSourceUploadJobs_VmImageBuildSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "VmImageBuildSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildSourceUploadJobs_ActorUserId",
                table: "VmImageBuildSourceUploadJobs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildSourceUploadJobs_SourceId",
                table: "VmImageBuildSourceUploadJobs",
                column: "SourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VmImageBuildSourceUploadJobs");
        }
    }
}
