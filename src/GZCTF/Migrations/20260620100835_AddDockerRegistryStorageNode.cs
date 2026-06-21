using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddDockerRegistryStorageNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStorageNode",
                table: "WorkerNodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RegistryPort",
                table: "WorkerNodes",
                type: "integer",
                nullable: false,
                defaultValue: 5000);

            migrationBuilder.CreateTable(
                name: "DockerRegistryMigrationTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRegistry = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TargetRegistry = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    CompletedItems = table.Column<int>(type: "integer", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockerRegistryMigrationTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DockerRegistryMigrationTasks_WorkerNodes_TargetNodeId",
                        column: x => x.TargetNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DockerRegistryMigrationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    SourceImage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TargetImage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TargetDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BytesTransferred = table.Column<long>(type: "bigint", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockerRegistryMigrationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DockerRegistryMigrationItems_DockerRegistryMigrationTasks_T~",
                        column: x => x.TaskId,
                        principalTable: "DockerRegistryMigrationTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DockerRegistryMigrationItems_ImageTemplates_ImageTemplateId",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DockerRegistryMigrationItems_ImageTemplateId",
                table: "DockerRegistryMigrationItems",
                column: "ImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_DockerRegistryMigrationItems_Status",
                table: "DockerRegistryMigrationItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DockerRegistryMigrationItems_TaskId",
                table: "DockerRegistryMigrationItems",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_DockerRegistryMigrationTasks_Status",
                table: "DockerRegistryMigrationTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DockerRegistryMigrationTasks_TargetNodeId",
                table: "DockerRegistryMigrationTasks",
                column: "TargetNodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DockerRegistryMigrationItems");

            migrationBuilder.DropTable(
                name: "DockerRegistryMigrationTasks");

            migrationBuilder.DropColumn(
                name: "IsStorageNode",
                table: "WorkerNodes");

            migrationBuilder.DropColumn(
                name: "RegistryPort",
                table: "WorkerNodes");
        }
    }
}
