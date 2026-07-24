using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddVmPreparedArtifactControlPlaneAndFactoryCutover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabBootstrapExecutions_RuntimeId_Generation_AssetId_Pro~",
                table: "TeamLabBootstrapExecutions");

            migrationBuilder.AlterColumn<int>(
                name: "Attempt",
                table: "TeamLabBootstrapExecutions",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<long>(
                name: "BootEpoch",
                table: "TeamLabBootstrapExecutions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionId",
                table: "TeamLabBootstrapExecutions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabBootstrapExecutions"
                        GROUP BY "RuntimeId", "Generation", "AssetId", "ProfileId", "ProfileVersion", "StepKey"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Duplicate historical TeamLab bootstrap executions must be reconciled before migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                UPDATE "TeamLabBootstrapExecutions"
                SET "Attempt" = 1,
                    "ExecutionId" = (
                        SUBSTRING(MD5('gzctf-bootstrap-execution:' || "Id"::text), 1, 8) || '-' ||
                        SUBSTRING(MD5('gzctf-bootstrap-execution:' || "Id"::text), 9, 4) || '-' ||
                        '5' || SUBSTRING(MD5('gzctf-bootstrap-execution:' || "Id"::text), 14, 3) || '-' ||
                        '8' || SUBSTRING(MD5('gzctf-bootstrap-execution:' || "Id"::text), 18, 3) || '-' ||
                        SUBSTRING(MD5('gzctf-bootstrap-execution:' || "Id"::text), 21, 12)
                    )::uuid
                WHERE "ExecutionId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ExecutionId",
                table: "TeamLabBootstrapExecutions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreparedArtifactId",
                table: "ImageTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "VmPreparationStatus",
                table: "ImageTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "GuestProtocolVersion",
                table: "ImageTemplateCapabilityCertifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreparationContractVersion",
                table: "ImageTemplateCapabilityCertifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManifestSignature",
                table: "BootstrapProfileVersions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SigningPublicKeyPem",
                table: "BootstrapProfileVersions",
                type: "text",
                nullable: false,
                defaultValue: "");

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
                    table.ForeignKey(
                        name: "FK_VmPreparedArtifacts_ImageTemplates_SourceImageTemplateId",
                        column: x => x.SourceImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VmImagePreparationJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceImageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    FactoryVersion = table.Column<int>(type: "integer", nullable: false),
                    PreparationContractVersion = table.Column<int>(type: "integer", nullable: false),
                    GuestProtocolVersion = table.Column<int>(type: "integer", nullable: false),
                    OSType = table.Column<byte>(type: "smallint", nullable: false),
                    Mode = table.Column<byte>(type: "smallint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceImageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProtectedOnboardingSecret = table.Column<string>(type: "text", nullable: true),
                    PreparedArtifactId = table.Column<long>(type: "bigint", nullable: true),
                    DerivedImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_TeamLabBootstrapExecutions_ExecutionId",
                table: "TeamLabBootstrapExecutions",
                column: "ExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabBootstrapExecutions_RuntimeId_Generation_AssetId_Pro~",
                table: "TeamLabBootstrapExecutions",
                columns: new[] { "RuntimeId", "Generation", "AssetId", "ProfileId", "ProfileVersion", "StepKey" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeamLabBootstrapExecutions_Attempt",
                table: "TeamLabBootstrapExecutions",
                sql: "\"Attempt\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplates_PreparedArtifactId",
                table: "ImageTemplates",
                column: "PreparedArtifactId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageTemplateCapabilityCertifications_ImageTemplateId_Prepa~",
                table: "ImageTemplateCapabilityCertifications",
                columns: new[] { "ImageTemplateId", "PreparationContractVersion", "GuestProtocolVersion" });

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

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_ArtifactDigest",
                table: "VmPreparedArtifacts",
                column: "ArtifactDigest");

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_PublicId",
                table: "VmPreparedArtifacts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_SourceImageHash_FactoryVersion_GuestPro~",
                table: "VmPreparedArtifacts",
                columns: new[] { "SourceImageHash", "FactoryVersion", "GuestProtocolVersion", "OSType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VmPreparedArtifacts_SourceImageTemplateId",
                table: "VmPreparedArtifacts",
                column: "SourceImageTemplateId");

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

            migrationBuilder.DropTable(
                name: "VmImagePreparationJobs");

            migrationBuilder.DropTable(
                name: "VmPreparedArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabBootstrapExecutions_ExecutionId",
                table: "TeamLabBootstrapExecutions");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabBootstrapExecutions_RuntimeId_Generation_AssetId_Pro~",
                table: "TeamLabBootstrapExecutions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TeamLabBootstrapExecutions_Attempt",
                table: "TeamLabBootstrapExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ImageTemplates_PreparedArtifactId",
                table: "ImageTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ImageTemplateCapabilityCertifications_ImageTemplateId_Prepa~",
                table: "ImageTemplateCapabilityCertifications");

            migrationBuilder.DropColumn(
                name: "BootEpoch",
                table: "TeamLabBootstrapExecutions");

            migrationBuilder.DropColumn(
                name: "ExecutionId",
                table: "TeamLabBootstrapExecutions");

            migrationBuilder.DropColumn(
                name: "PreparedArtifactId",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "VmPreparationStatus",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "GuestProtocolVersion",
                table: "ImageTemplateCapabilityCertifications");

            migrationBuilder.DropColumn(
                name: "PreparationContractVersion",
                table: "ImageTemplateCapabilityCertifications");

            migrationBuilder.DropColumn(
                name: "ManifestSignature",
                table: "BootstrapProfileVersions");

            migrationBuilder.DropColumn(
                name: "SigningPublicKeyPem",
                table: "BootstrapProfileVersions");

            migrationBuilder.AlterColumn<int>(
                name: "Attempt",
                table: "TeamLabBootstrapExecutions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabBootstrapExecutions_RuntimeId_Generation_AssetId_Pro~",
                table: "TeamLabBootstrapExecutions",
                columns: new[] { "RuntimeId", "Generation", "AssetId", "ProfileId", "ProfileVersion", "StepKey", "Attempt" },
                unique: true);
        }
    }
}
