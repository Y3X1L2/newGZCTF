using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabCapabilityResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLabConnectors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    ControlScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupportsSharedUse = table.Column<bool>(type: "boolean", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    AttachmentReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Health = table.Column<byte>(type: "smallint", nullable: false),
                    HealthObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabConnectors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabConnectors_TeamLabControlScopes_ControlScopeId",
                        column: x => x.ControlScopeId,
                        principalTable: "TeamLabControlScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabDevicePackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtifactKind = table.Column<byte>(type: "smallint", nullable: false),
                    ArtifactReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SupportedAssetKindsJson = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CpuMillis = table.Column<int>(type: "integer", nullable: false),
                    MemoryMib = table.Column<int>(type: "integer", nullable: false),
                    StorageGib = table.Column<int>(type: "integer", nullable: false),
                    PortsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ParameterSchemaJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    HealthDeclarationJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ProtocolEventTypesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabDevicePackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabLinkPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    ControlScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    NetworkKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssetKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    ParametersJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    RecoverAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecoverOrigin = table.Column<byte>(type: "smallint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabLinkPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabLinkPolicies_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabConnectorLeases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorId = table.Column<int>(type: "integer", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleaseReason = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabConnectorLeases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabConnectorLeases_TeamLabConnectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "TeamLabConnectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabConnectorLeases_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabConnectorLeases_ConnectorId_RuntimeId",
                table: "TeamLabConnectorLeases",
                columns: new[] { "ConnectorId", "RuntimeId" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabConnectorLeases_ConnectorId_Slot",
                table: "TeamLabConnectorLeases",
                columns: new[] { "ConnectorId", "Slot" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabConnectorLeases_PublicId",
                table: "TeamLabConnectorLeases",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabConnectorLeases_RuntimeId",
                table: "TeamLabConnectorLeases",
                column: "RuntimeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabConnectors_ControlScopeId",
                table: "TeamLabConnectors",
                column: "ControlScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabConnectors_Name",
                table: "TeamLabConnectors",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabConnectors_PublicId",
                table: "TeamLabConnectors",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabDevicePackages_IsEnabled_Name",
                table: "TeamLabDevicePackages",
                columns: new[] { "IsEnabled", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabDevicePackages_Name_Version",
                table: "TeamLabDevicePackages",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabDevicePackages_PublicId",
                table: "TeamLabDevicePackages",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabLinkPolicies_PublicId",
                table: "TeamLabLinkPolicies",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabLinkPolicies_RuntimeId_NetworkKey_AssetKey_Kind",
                table: "TeamLabLinkPolicies",
                columns: new[] { "RuntimeId", "NetworkKey", "AssetKey", "Kind" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabLinkPolicies_Status_RecoverAt",
                table: "TeamLabLinkPolicies",
                columns: new[] { "Status", "RecoverAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabConnectorLeases");

            migrationBuilder.DropTable(
                name: "TeamLabDevicePackages");

            migrationBuilder.DropTable(
                name: "TeamLabLinkPolicies");

            migrationBuilder.DropTable(
                name: "TeamLabConnectors");
        }
    }
}
