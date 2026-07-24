using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class FinalizePhaseNineVmBuildAndScenarioArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "VmPreparedArtifacts") OR
                       EXISTS (SELECT 1 FROM "VmImagePreparationJobs") THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'P0001',
                            MESSAGE = 'phase9_vm_factory_data_requires_explicit_cleanup',
                            DETAIL = 'Legacy VM preparation records exist. Export or remove only failed preparation operations before applying this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ImageTemplates_VmPreparedArtifacts_PreparedArtifactId",
                table: "ImageTemplates");

            migrationBuilder.Sql("UPDATE \"ImageTemplates\" SET \"PreparedArtifactId\" = NULL;");

            migrationBuilder.DropTable(
                name: "VmImagePreparationJobs");

            migrationBuilder.DropTable(
                name: "VmPreparedArtifacts");

            migrationBuilder.DropColumn(
                name: "VmPreparationStatus",
                table: "ImageTemplates");

            migrationBuilder.AddColumn<byte>(
                name: "VmRuntimeMode",
                table: "ImageTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<bool>(
                name: "BakeAtPublish",
                table: "TeamLabTopologyAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsScenarioBuild",
                table: "TeamLabRuntimes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "VmArtifactStatus",
                table: "ImageTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "VmNetworkMode",
                table: "ImageTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "TeamLabReleaseAssetArtifacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    ScenarioImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    BakeRuntimeId = table.Column<int>(type: "integer", nullable: true),
                    CommitOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    BuildIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtifactDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArtifactSize = table.Column<long>(type: "bigint", nullable: false),
                    RegistryAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RegistryRepository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegistryTag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabReleaseAssetArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabReleaseAssetArtifacts_ImageTemplates_ScenarioImageTe~",
                        column: x => x.ScenarioImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabReleaseAssetArtifacts_ImageTemplates_SourceImageTemp~",
                        column: x => x.SourceImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabReleaseAssetArtifacts_TeamLabRuntimes_BakeRuntimeId",
                        column: x => x.BakeRuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabReleaseAssetArtifacts_TeamLabTopologyReleases_Releas~",
                        column: x => x.ReleaseId,
                        principalTable: "TeamLabTopologyReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VmImageBuildSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    OsFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Architecture = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    RegistryAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RegistryRepository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegistryTag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "VmPreparedArtifacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    SourceDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecipeVersion = table.Column<int>(type: "integer", nullable: false),
                    RecipeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GuestProtocolVersion = table.Column<int>(type: "integer", nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ArtifactDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArtifactSize = table.Column<long>(type: "bigint", nullable: false),
                    BuiltOnWorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistryAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RegistryRepository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegistryTag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmPreparedArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VmPreparedArtifacts_VmImageBuildSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "VmImageBuildSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VmPreparedArtifacts_WorkerNodes_BuiltOnWorkerNodeId",
                        column: x => x.BuiltOnWorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VmImageBuildJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    RecipeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecipeVersion = table.Column<int>(type: "integer", nullable: false),
                    RecipeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    GuestProtocolVersion = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparedArtifactId = table.Column<long>(type: "bigint", nullable: true),
                    DerivedImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_TeamLabReleaseAssetArtifacts_BakeRuntimeId",
                table: "TeamLabReleaseAssetArtifacts",
                column: "BakeRuntimeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabReleaseAssetArtifacts_BuildIdentity",
                table: "TeamLabReleaseAssetArtifacts",
                column: "BuildIdentity");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabReleaseAssetArtifacts_ReleaseId_AssetKey",
                table: "TeamLabReleaseAssetArtifacts",
                columns: new[] { "ReleaseId", "AssetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabReleaseAssetArtifacts_ScenarioImageTemplateId",
                table: "TeamLabReleaseAssetArtifacts",
                column: "ScenarioImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabReleaseAssetArtifacts_SourceImageTemplateId",
                table: "TeamLabReleaseAssetArtifacts",
                column: "SourceImageTemplateId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_ImageTemplates_VmPreparedArtifacts_PreparedArtifactId",
                table: "ImageTemplates",
                column: "PreparedArtifactId",
                principalTable: "VmPreparedArtifacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImageTemplates_VmPreparedArtifacts_PreparedArtifactId",
                table: "ImageTemplates");

            migrationBuilder.Sql("UPDATE \"ImageTemplates\" SET \"PreparedArtifactId\" = NULL;");

            migrationBuilder.DropTable(
                name: "TeamLabReleaseAssetArtifacts");

            migrationBuilder.DropTable(
                name: "VmImageBuildJobs");

            migrationBuilder.DropTable(
                name: "VmPreparedArtifacts");

            migrationBuilder.DropTable(
                name: "VmImageBuildSources");

            migrationBuilder.DropColumn(
                name: "BakeAtPublish",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "IsScenarioBuild",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "VmArtifactStatus",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "VmNetworkMode",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "VmRuntimeMode",
                table: "ImageTemplates");

            migrationBuilder.AddColumn<byte>(
                name: "VmPreparationStatus",
                table: "ImageTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "VmPreparedArtifacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    SourceImageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FactoryVersion = table.Column<int>(type: "integer", nullable: false),
                    PreparationContractVersion = table.Column<int>(type: "integer", nullable: false),
                    GuestProtocolVersion = table.Column<int>(type: "integer", nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ArtifactDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArtifactSize = table.Column<long>(type: "bigint", nullable: false),
                    RegistryAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RegistryRepository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegistryTag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmPreparedArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VmImagePreparationJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DerivedImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    PreparedArtifactId = table.Column<long>(type: "bigint", nullable: true),
                    SourceImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FactoryVersion = table.Column<int>(type: "integer", nullable: false),
                    GuestProtocolVersion = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<byte>(type: "smallint", nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    PreparationContractVersion = table.Column<int>(type: "integer", nullable: false),
                    ProtectedOnboardingSecret = table.Column<string>(type: "text", nullable: true),
                    SourceImageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmImagePreparationJobs", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_VmImagePreparationJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VmImagePreparationJobs_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VmImagePreparationJobs_ImageTemplates_DerivedImageTemplateId",
                        column: x => x.DerivedImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VmImagePreparationJobs_ImageTemplates_SourceImageTemplateId",
                        column: x => x.SourceImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VmImagePreparationJobs_VmPreparedArtifacts_PreparedArtifact~",
                        column: x => x.PreparedArtifactId,
                        principalTable: "VmPreparedArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VmImagePreparationJobs_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_SourceImageHash_FactoryVersion_GuestPro~",
                table: "VmPreparedArtifacts",
                columns: new[] { "SourceImageHash", "FactoryVersion", "GuestProtocolVersion", "OSType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_SourceImageTemplateId",
                table: "VmPreparedArtifacts",
                column: "SourceImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImagePreparationJobs_ActorUserId",
                table: "VmImagePreparationJobs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImagePreparationJobs_DerivedImageTemplateId",
                table: "VmImagePreparationJobs",
                column: "DerivedImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImagePreparationJobs_PreparedArtifactId",
                table: "VmImagePreparationJobs",
                column: "PreparedArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_VmImagePreparationJobs_SourceImageTemplateId_FactoryVersion~",
                table: "VmImagePreparationJobs",
                columns: new[] { "SourceImageTemplateId", "FactoryVersion", "GuestProtocolVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_VmImagePreparationJobs_WorkerNodeId",
                table: "VmImagePreparationJobs",
                column: "WorkerNodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_VmPreparedArtifacts_ImageTemplates_SourceImageTemplateId",
                table: "VmPreparedArtifacts",
                column: "SourceImageTemplateId",
                principalTable: "ImageTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ImageTemplates_VmPreparedArtifacts_PreparedArtifactId",
                table: "ImageTemplates",
                column: "PreparedArtifactId",
                principalTable: "VmPreparedArtifacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
