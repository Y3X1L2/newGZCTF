using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddBootstrapProfilesAndCertifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BootstrapProfileOperationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<byte>(type: "smallint", nullable: false),
                    ProfilePublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: true),
                    StagedArtifactPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ArtifactDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ArtifactSize = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BootstrapProfileOperationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BootstrapProfileOperationJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BootstrapProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BootstrapProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BootstrapProfiles_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImageTemplateCapabilityCertifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    ImageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProbeKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProbeStep = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorDetail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CertifiedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CertifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageTemplateCapabilityCertifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageTemplateCapabilityCertifications_AspNetUsers_Certified~",
                        column: x => x.CertifiedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImageTemplateCapabilityCertifications_ImageTemplates_ImageT~",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImageTemplateCapabilityCertifications_WorkerNodes_WorkerNod~",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImageTemplateCertificationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProbeKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageTemplateCertificationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageTemplateCertificationJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BootstrapProfileVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ManifestDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArtifactDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArtifactSize = table.Column<long>(type: "bigint", nullable: false),
                    RegistryAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RegistryRepository = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RegistryTag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BootstrapProfileVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BootstrapProfileVersions_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BootstrapProfileVersions_BootstrapProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "BootstrapProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BootstrapProfileDistributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersionId = table.Column<long>(type: "bigint", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    LocalPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BootstrapProfileDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BootstrapProfileDistributions_BootstrapProfileVersions_Prof~",
                        column: x => x.ProfileVersionId,
                        principalTable: "BootstrapProfileVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BootstrapProfileDistributions_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfileDistributions_ProfileVersionId_WorkerNodeId",
                table: "BootstrapProfileDistributions",
                columns: new[] { "ProfileVersionId", "WorkerNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfileDistributions_WorkerNodeId",
                table: "BootstrapProfileDistributions",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfileOperationJobs_OperationId",
                table: "BootstrapProfileOperationJobs",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfiles_CreatedById",
                table: "BootstrapProfiles",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfiles_PublicId",
                table: "BootstrapProfiles",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfileVersions_ArtifactDigest",
                table: "BootstrapProfileVersions",
                column: "ArtifactDigest");

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfileVersions_CreatedById",
                table: "BootstrapProfileVersions",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapProfileVersions_ProfileId_Version",
                table: "BootstrapProfileVersions",
                columns: new[] { "ProfileId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplateCapabilityCertifications_CertifiedById",
                table: "ImageTemplateCapabilityCertifications",
                column: "CertifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplateCapabilityCertifications_ImageTemplateId_Image~",
                table: "ImageTemplateCapabilityCertifications",
                columns: new[] { "ImageTemplateId", "ImageHash", "EvidenceDigest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplateCapabilityCertifications_WorkerNodeId",
                table: "ImageTemplateCapabilityCertifications",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplateCertificationJobs_OperationId",
                table: "ImageTemplateCertificationJobs",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BootstrapProfileDistributions");

            migrationBuilder.DropTable(
                name: "BootstrapProfileOperationJobs");

            migrationBuilder.DropTable(
                name: "ImageTemplateCapabilityCertifications");

            migrationBuilder.DropTable(
                name: "ImageTemplateCertificationJobs");

            migrationBuilder.DropTable(
                name: "BootstrapProfileVersions");

            migrationBuilder.DropTable(
                name: "BootstrapProfiles");
        }
    }
}
