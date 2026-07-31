using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPhaseNineTeamLabNetworking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ViaAssetKey",
                table: "TeamLabTopologyConnections",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(63)",
                oldMaxLength: 63);

            migrationBuilder.AddColumn<byte>(
                name: "Direction",
                table: "TeamLabTopologyConnections",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "ViaNodeKey",
                table: "TeamLabTopologyConnections",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BootstrapJson",
                table: "TeamLabTopologyAssets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "EndpointObservation",
                table: "TeamLabTopologyAssets",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "Stateless",
                table: "TeamLabTopologyAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DependenciesJson",
                table: "TeamLabTopologies",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "InfrastructureJson",
                table: "TeamLabTopologies",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "ObservationJson",
                table: "TeamLabTopologies",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "Scope",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "NetworkKey",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamLabBootstrapExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    AssetId = table.Column<int>(type: "integer", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    StepKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    InputDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OutputDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabBootstrapExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabBootstrapExecutions_TeamLabRuntimeAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "TeamLabRuntimeAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabBootstrapExecutions_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabFabricLinkLeases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    ShardId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocatedCidr = table.Column<IPNetwork>(type: "cidr", nullable: false),
                    HubAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NodeAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabFabricLinkLeases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabFabricLinkLeases_TeamLabRuntimeShards_ShardId",
                        column: x => x.ShardId,
                        principalTable: "TeamLabRuntimeShards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabFabricLinkLeases_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabFabricLinkLeases_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeDependencyStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    AssetKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    DependsOnKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Condition = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    SatisfiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeDependencyStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeDependencyStates_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeInfrastructure",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    NetworkKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    InterfaceSummaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    ConnectionSummaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    DesiredStateDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeInfrastructure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeInfrastructure_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeInfrastructureFragments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    InfrastructureId = table.Column<int>(type: "integer", nullable: false),
                    ShardId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FragmentKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InterfaceSummaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    NativeResourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DesiredStateDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeInfrastructureFragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeInfrastructureFragments_TeamLabRuntimeInfrast~",
                        column: x => x.InfrastructureId,
                        principalTable: "TeamLabRuntimeInfrastructure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeInfrastructureFragments_TeamLabRuntimeShards_~",
                        column: x => x.ShardId,
                        principalTable: "TeamLabRuntimeShards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeInfrastructureFragments_WorkerNodes_WorkerNod~",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabObservationPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShardId = table.Column<int>(type: "integer", nullable: true),
                    NetworkId = table.Column<int>(type: "integer", nullable: true),
                    InfrastructureFragmentId = table.Column<int>(type: "integer", nullable: true),
                    AssetId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    InterfaceToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DesiredStateDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    DroppedPackets = table.Column<long>(type: "bigint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabObservationPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationPoints_TeamLabRuntimeAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "TeamLabRuntimeAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationPoints_TeamLabRuntimeInfrastructureFragme~",
                        column: x => x.InfrastructureFragmentId,
                        principalTable: "TeamLabRuntimeInfrastructureFragments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationPoints_TeamLabRuntimeNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "TeamLabRuntimeNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationPoints_TeamLabRuntimeShards_ShardId",
                        column: x => x.ShardId,
                        principalTable: "TeamLabRuntimeShards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationPoints_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabObservationPoints_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTrafficCaptureSegments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureJobId = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationPointId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ObjectPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CapturedBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTrafficCaptureSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCaptureSegments_TeamLabObservationPoints_Obse~",
                        column: x => x.ObservationPointId,
                        principalTable: "TeamLabObservationPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCaptureSegments_TeamLabTrafficCaptureJobs_Cap~",
                        column: x => x.CaptureJobId,
                        principalTable: "TeamLabTrafficCaptureJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabTrafficCaptureSegments_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabBootstrapExecutions_AssetId",
                table: "TeamLabBootstrapExecutions",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabBootstrapExecutions_RuntimeId_Generation_AssetId_Pro~",
                table: "TeamLabBootstrapExecutions",
                columns: new[] { "RuntimeId", "Generation", "AssetId", "ProfileId", "ProfileVersion", "StepKey", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabFabricLinkLeases_ReleasedAt",
                table: "TeamLabFabricLinkLeases",
                column: "ReleasedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabFabricLinkLeases_RuntimeId_Generation_ShardId",
                table: "TeamLabFabricLinkLeases",
                columns: new[] { "RuntimeId", "Generation", "ShardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabFabricLinkLeases_ShardId",
                table: "TeamLabFabricLinkLeases",
                column: "ShardId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabFabricLinkLeases_WorkerNodeId",
                table: "TeamLabFabricLinkLeases",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationPoints_AssetId",
                table: "TeamLabObservationPoints",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationPoints_InfrastructureFragmentId",
                table: "TeamLabObservationPoints",
                column: "InfrastructureFragmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationPoints_NetworkId",
                table: "TeamLabObservationPoints",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationPoints_PublicId",
                table: "TeamLabObservationPoints",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationPoints_RuntimeId_Generation_WorkerNodeId_~",
                table: "TeamLabObservationPoints",
                columns: new[] { "RuntimeId", "Generation", "WorkerNodeId", "InterfaceToken", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationPoints_ShardId",
                table: "TeamLabObservationPoints",
                column: "ShardId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabObservationPoints_WorkerNodeId",
                table: "TeamLabObservationPoints",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureJobs_PublicId",
                table: "TeamLabTrafficCaptureJobs",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureSegments_CaptureJobId_ObservationPoint~",
                table: "TeamLabTrafficCaptureSegments",
                columns: new[] { "CaptureJobId", "ObservationPointId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureSegments_ObservationPointId",
                table: "TeamLabTrafficCaptureSegments",
                column: "ObservationPointId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureSegments_PublicId",
                table: "TeamLabTrafficCaptureSegments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureSegments_Status_UpdatedAt",
                table: "TeamLabTrafficCaptureSegments",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTrafficCaptureSegments_WorkerNodeId",
                table: "TeamLabTrafficCaptureSegments",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeDependencyStates_RuntimeId_Generation_AssetKe~",
                table: "TeamLabRuntimeDependencyStates",
                columns: new[] { "RuntimeId", "Generation", "AssetKey", "DependsOnKey", "Condition" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeInfrastructure_PublicId",
                table: "TeamLabRuntimeInfrastructure",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeInfrastructure_RuntimeId_Generation_TopologyK~",
                table: "TeamLabRuntimeInfrastructure",
                columns: new[] { "RuntimeId", "Generation", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeInfrastructureFragments_InfrastructureId_Shar~",
                table: "TeamLabRuntimeInfrastructureFragments",
                columns: new[] { "InfrastructureId", "ShardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeInfrastructureFragments_PublicId",
                table: "TeamLabRuntimeInfrastructureFragments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeInfrastructureFragments_ShardId",
                table: "TeamLabRuntimeInfrastructureFragments",
                column: "ShardId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeInfrastructureFragments_WorkerNodeId",
                table: "TeamLabRuntimeInfrastructureFragments",
                column: "WorkerNodeId");

            migrationBuilder.Sql("""
                ALTER TABLE "TeamLabFabricLinkLeases"
                ADD CONSTRAINT "EX_TeamLabFabricLinkLeases_ActiveCidr"
                EXCLUDE USING gist ("AllocatedCidr" inet_ops WITH &&)
                WHERE ("ReleasedAt" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabTrafficCaptureSegments");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTrafficCaptureJobs_PublicId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropTable(
                name: "TeamLabBootstrapExecutions");

            migrationBuilder.DropTable(
                name: "TeamLabFabricLinkLeases");

            migrationBuilder.DropTable(
                name: "TeamLabObservationPoints");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeDependencyStates");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeInfrastructureFragments");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeInfrastructure");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "TeamLabTopologyConnections");

            migrationBuilder.DropColumn(
                name: "ViaNodeKey",
                table: "TeamLabTopologyConnections");

            migrationBuilder.DropColumn(
                name: "BootstrapJson",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "EndpointObservation",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "Stateless",
                table: "TeamLabTopologyAssets");

            migrationBuilder.DropColumn(
                name: "DependenciesJson",
                table: "TeamLabTopologies");

            migrationBuilder.DropColumn(
                name: "InfrastructureJson",
                table: "TeamLabTopologies");

            migrationBuilder.DropColumn(
                name: "ObservationJson",
                table: "TeamLabTopologies");

            migrationBuilder.DropColumn(
                name: "NetworkKey",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.AlterColumn<string>(
                name: "Scope",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "ViaAssetKey",
                table: "TeamLabTopologyConnections",
                type: "character varying(63)",
                maxLength: 63,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(63)",
                oldMaxLength: 63,
                oldNullable: true);
        }
    }
}
