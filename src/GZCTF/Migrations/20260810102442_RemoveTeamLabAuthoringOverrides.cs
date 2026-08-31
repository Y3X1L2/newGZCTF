using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTeamLabAuthoringOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"ImageTemplates\" SET \"VmRuntimeMode\" = 1 WHERE \"VmRuntimeMode\" = 2;");

            migrationBuilder.DropTable(
                name: "TeamLabReleaseAssetArtifacts");

            migrationBuilder.DropColumn(
                name: "BakeAtPublish",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "EnvironmentJson",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "RoutingEnabled",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "StartCommand",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "Stateless",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "IsScenarioBuild",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "Stateless",
                table: "TeamLabRuntimeAssets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BakeAtPublish",
                table: "TeamLabTopologyAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentJson",
                table: "TeamLabTopologyAssets",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RoutingEnabled",
                table: "TeamLabTopologyAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StartCommand",
                table: "TeamLabTopologyAssets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Stateless",
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

            migrationBuilder.AddColumn<bool>(
                name: "Stateless",
                table: "TeamLabRuntimeAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TeamLabReleaseAssetArtifacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BakeRuntimeId = table.Column<int>(type: "integer", nullable: true),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    SourceImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    ArtifactDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArtifactSize = table.Column<long>(type: "bigint", nullable: false),
                    AssetKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BakeAttemptOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuildIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommitOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    EvidenceDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RegistryAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RegistryRepository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegistryTag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false)
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
        }
    }
}
