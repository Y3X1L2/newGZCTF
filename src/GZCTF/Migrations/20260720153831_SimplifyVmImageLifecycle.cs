using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyVmImageLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VmPreparedArtifacts_VmImageBuildSources_SourceId",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropForeignKey(
                name: "FK_VmPreparedArtifacts_WorkerNodes_BuiltOnWorkerNodeId",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropTable(
                name: "VmImageBuildJobs");

            migrationBuilder.DropTable(
                name: "VmImageBuildSourceUploadJobs");

            migrationBuilder.DropTable(
                name: "VmImageBuildSources");

            migrationBuilder.DropIndex(
                name: "IX_VmPreparedArtifacts_BuiltOnWorkerNodeId",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_VmPreparedArtifacts_SourceDigest_RecipeDigest_PackageDigest~",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_VmPreparedArtifacts_SourceId",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "BuiltOnWorkerNodeId",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "GuestProtocolVersion",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "PackageDigest",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "RecipeDigest",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "RecipeId",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "RecipeVersion",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "SourceDigest",
                table: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "VmPreparedArtifacts");

            migrationBuilder.AddColumn<byte>(
                name: "RequestedVmNetworkMode",
                table: "ImageImportJobs",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedVmNetworkMode",
                table: "ImageImportJobs");

            migrationBuilder.AddColumn<Guid>(
                name: "BuiltOnWorkerNodeId",
                table: "VmPreparedArtifacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GuestProtocolVersion",
                table: "VmPreparedArtifacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PackageDigest",
                table: "VmPreparedArtifacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecipeDigest",
                table: "VmPreparedArtifacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecipeId",
                table: "VmPreparedArtifacts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RecipeVersion",
                table: "VmPreparedArtifacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceDigest",
                table: "VmPreparedArtifacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SourceId",
                table: "VmPreparedArtifacts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "VmImageBuildSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Architecture = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    OsFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistryAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RegistryRepository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegistryTag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmImageBuildSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VmImageBuildSources_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VmImageBuildJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DerivedImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    PreparedArtifactId = table.Column<long>(type: "bigint", nullable: true),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GuestProtocolVersion = table.Column<int>(type: "integer", nullable: false),
                    PackageDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecipeVersion = table.Column<int>(type: "integer", nullable: false),
                    RequestedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmImageBuildJobs", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_VmImageBuildJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VmImageBuildJobs_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VmImageBuildJobs_ImageTemplates_DerivedImageTemplateId",
                        column: x => x.DerivedImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VmImageBuildJobs_VmImageBuildSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "VmImageBuildSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VmImageBuildJobs_VmPreparedArtifacts_PreparedArtifactId",
                        column: x => x.PreparedArtifactId,
                        principalTable: "VmPreparedArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VmImageBuildJobs_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VmImageBuildSourceUploadJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Architecture = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OsFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourcePublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    StagedPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
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
                name: "IX_VmPreparedArtifacts_BuiltOnWorkerNodeId",
                table: "VmPreparedArtifacts",
                column: "BuiltOnWorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_SourceDigest_RecipeDigest_PackageDigest~",
                table: "VmPreparedArtifacts",
                columns: new[] { "SourceDigest", "RecipeDigest", "PackageDigest", "GuestProtocolVersion", "OSType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_SourceId",
                table: "VmPreparedArtifacts",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildJobs_ActorUserId",
                table: "VmImageBuildJobs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildJobs_DerivedImageTemplateId",
                table: "VmImageBuildJobs",
                column: "DerivedImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildJobs_PreparedArtifactId",
                table: "VmImageBuildJobs",
                column: "PreparedArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildJobs_SourceId_RecipeDigest_GuestProtocolVersion",
                table: "VmImageBuildJobs",
                columns: new[] { "SourceId", "RecipeDigest", "GuestProtocolVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildJobs_WorkerNodeId",
                table: "VmImageBuildJobs",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildSources_CreatedById",
                table: "VmImageBuildSources",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildSources_Digest",
                table: "VmImageBuildSources",
                column: "Digest");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildSources_PublicId",
                table: "VmImageBuildSources",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildSourceUploadJobs_ActorUserId",
                table: "VmImageBuildSourceUploadJobs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImageBuildSourceUploadJobs_SourceId",
                table: "VmImageBuildSourceUploadJobs",
                column: "SourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_VmPreparedArtifacts_VmImageBuildSources_SourceId",
                table: "VmPreparedArtifacts",
                column: "SourceId",
                principalTable: "VmImageBuildSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VmPreparedArtifacts_WorkerNodes_BuiltOnWorkerNodeId",
                table: "VmPreparedArtifacts",
                column: "BuiltOnWorkerNodeId",
                principalTable: "WorkerNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
