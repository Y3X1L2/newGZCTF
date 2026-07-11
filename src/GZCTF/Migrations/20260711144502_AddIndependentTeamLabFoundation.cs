using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentTeamLabFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeShards_RuntimeId_WorkerNodeId",
                table: "TeamLabRuntimeShards");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeNetworks_RuntimeId_TopologyKey",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeAssets_RuntimeId_Kind_TopologyKey",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstSeenAt",
                table: "TeamLabTrafficFlows",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabTrafficFlows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "TeamLabTrafficFlows",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<long>(
                name: "Packets",
                table: "TeamLabTrafficFlows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabTrafficCaptureJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "TeamLabTrafficCaptureJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabRuntimeShards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "TeamLabRuntimeShards",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CreateRequestHash",
                table: "TeamLabRuntimes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "TeamLabRuntimes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntryShardId",
                table: "TeamLabRuntimes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "TeamLabRuntimes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabRuntimes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "TeamLabRuntimes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TopologyReleaseId",
                table: "TeamLabRuntimes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabRuntimeNetworks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "NetworkLeaseId",
                table: "TeamLabRuntimeNetworks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabRuntimeAssets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabPublicUdpMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "TeamLabEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ObjectiveId",
                table: "PenetrationSubmissions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PenetrationObjectives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TopologyAssetKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    IsDynamic = table.Column<bool>(type: "boolean", nullable: false),
                    StaticFlag = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: true),
                    FlagTemplate = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsCheckpoint = table.Column<bool>(type: "boolean", nullable: false),
                    PrerequisiteObjectiveKeysJson = table.Column<string>(type: "jsonb", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationObjectives_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationTeamRuntimeBindings",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationTeamRuntimeBindings", x => new { x.GameId, x.TeamId });
                    table.ForeignKey(
                        name: "FK_PenetrationTeamRuntimeBindings_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamRuntimeBindings_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamRuntimeBindings_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabAccessGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<byte>(type: "smallint", nullable: false),
                    ClientAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AllowedIps = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Dns = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProtectedPrivateKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ServerPublicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProtectedServerPrivateKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabAccessGrants_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeSecretEnvelopes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeSecretEnvelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeSecretEnvelopes_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTopologies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTopologies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologies_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTopologyAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TopologyId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    CpuUnits = table.Column<int>(type: "integer", nullable: false),
                    MemoryMiB = table.Column<int>(type: "integer", nullable: false),
                    StorageMiB = table.Column<int>(type: "integer", nullable: false),
                    ExposePort = table.Column<int>(type: "integer", nullable: true),
                    RoutingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EnvironmentJson = table.Column<string>(type: "jsonb", nullable: false),
                    StartCommand = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    HealthCheckKind = table.Column<byte>(type: "smallint", nullable: true),
                    HealthCheckPort = table.Column<int>(type: "integer", nullable: true),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTopologyAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyAssets_ImageTemplates_ImageTemplateId",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyAssets_TeamLabTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "TeamLabTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTopologyConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TopologyId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    FromNetworkKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    ToNetworkKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    ViaAssetKey = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTopologyConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyConnections_TeamLabTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "TeamLabTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTopologyNetworks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TopologyId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AddressPoolCidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuntimePrefixLength = table.Column<int>(type: "integer", nullable: false),
                    IsEntry = table.Column<bool>(type: "boolean", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTopologyNetworks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyNetworks_TeamLabTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "TeamLabTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTopologyReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TopologyId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CanonicalJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublishedById = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTopologyReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyReleases_AspNetUsers_PublishedById",
                        column: x => x.PublishedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyReleases_TeamLabTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "TeamLabTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabNetworkLeases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    TopologyNetworkId = table.Column<int>(type: "integer", nullable: false),
                    AllocatedCidr = table.Column<IPNetwork>(type: "cidr", nullable: false),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabNetworkLeases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabNetworkLeases_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabNetworkLeases_TeamLabTopologyNetworks_TopologyNetwor~",
                        column: x => x.TopologyNetworkId,
                        principalTable: "TeamLabTopologyNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabTopologyInterfaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssetId = table.Column<int>(type: "integer", nullable: false),
                    NetworkId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    HostOffset = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabTopologyInterfaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyInterfaces_TeamLabTopologyAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "TeamLabTopologyAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabTopologyInterfaces_TeamLabTopologyNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "TeamLabTopologyNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationGameLabBindings",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TopologyId = table.Column<int>(type: "integer", nullable: false),
                    ActiveReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaxResetCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationGameLabBindings", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_PenetrationGameLabBindings_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationGameLabBindings_TeamLabTopologies_TopologyId",
                        column: x => x.TopologyId,
                        principalTable: "TeamLabTopologies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenetrationGameLabBindings_TeamLabTopologyReleases_ActiveRe~",
                        column: x => x.ActiveReleaseId,
                        principalTable: "TeamLabTopologyReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS pgcrypto;
                CREATE EXTENSION IF NOT EXISTS btree_gist;

                UPDATE "TeamLabRuntimes"
                SET "PublicId" = gen_random_uuid(), "Generation" = 1
                WHERE "PublicId" = '00000000-0000-0000-0000-000000000000' OR "Generation" = 0;

                UPDATE "TeamLabRuntimeShards"
                SET "PublicId" = gen_random_uuid(), "Generation" = 1
                WHERE "PublicId" = '00000000-0000-0000-0000-000000000000' OR "Generation" = 0;

                UPDATE "TeamLabRuntimeNetworks" SET "Generation" = 1 WHERE "Generation" = 0;
                UPDATE "TeamLabRuntimeAssets" SET "Generation" = 1 WHERE "Generation" = 0;
                UPDATE "TeamLabPublicUdpMappings" SET "Generation" = 1 WHERE "Generation" = 0;
                UPDATE "TeamLabEvents" SET "Generation" = 1 WHERE "Generation" = 0;
                UPDATE "TeamLabTrafficFlows"
                SET "Generation" = 1,
                    "FirstSeenAt" = "CapturedAt",
                    "LastSeenAt" = "CapturedAt"
                WHERE "Generation" = 0;
                UPDATE "TeamLabTrafficCaptureJobs"
                SET "PublicId" = gen_random_uuid(), "Generation" = 1
                WHERE "PublicId" = '00000000-0000-0000-0000-000000000000' OR "Generation" = 0;

                DO $$
                BEGIN
                    IF (SELECT count(*) FROM "PenetrationNetworks") > 4096 THEN
                        RAISE EXCEPTION 'TeamLab migration supports at most 4096 legacy networks; split the migration batch.';
                    END IF;
                END $$;

                INSERT INTO "TeamLabTopologies"
                    ("Id", "PublicId", "OwnerUserId", "Name", "Revision", "SchemaVersion", "CreatedAt", "UpdatedAt")
                SELECT pc."Id", gen_random_uuid(), g."OwnerId",
                       left(g."Title" || ' TeamLab', 128), 1, 1,
                       COALESCE(pc."PublishedAt", pc."UpdatedAt"), pc."UpdatedAt"
                FROM "PenetrationConfigs" pc
                JOIN "Games" g ON g."Id" = pc."GameId";

                WITH ranked AS (
                    SELECT pn.*, pc."NetworkSubnetPrefix",
                           row_number() OVER (ORDER BY pn."ConfigId", pn."OrderIndex", pn."Id") - 1 AS ordinal
                    FROM "PenetrationNetworks" pn
                    JOIN "PenetrationConfigs" pc ON pc."Id" = pn."ConfigId"
                )
                INSERT INTO "TeamLabTopologyNetworks"
                    ("Id", "TopologyId", "Key", "Name", "AddressPoolCidr", "RuntimePrefixLength", "IsEntry", "OrderIndex")
                SELECT "Id", "ConfigId", "TopologyKey", "Name",
                       ((inet '10.0.0.0' + (ordinal * 4096)::bigint)::text || '/20'),
                       LEAST(29, GREATEST(21, "NetworkSubnetPrefix")), "IsEntry", "OrderIndex"
                FROM ranked;

                INSERT INTO "TeamLabTopologyAssets"
                    ("Id", "TopologyId", "Key", "Name", "Kind", "ImageTemplateId", "CpuUnits", "MemoryMiB",
                     "StorageMiB", "ExposePort", "RoutingEnabled", "EnvironmentJson", "StartCommand",
                     "HealthCheckKind", "HealthCheckPort", "OrderIndex")
                SELECT pn."Id", pn."ConfigId", pn."TopologyKey", pn."Name",
                       CASE WHEN it."ImageType" = 0 THEN 0 ELSE 1 END,
                       pn."ImageTemplateId", GREATEST(1, pn."CpuCount"), GREATEST(1, pn."MemoryLimit"),
                       GREATEST(1, pn."StorageLimit"), NULLIF(pn."ExposePort", 0), pn."AllowRouting",
                       COALESCE(NULLIF(pn."EnvironmentVariables", ''), '{}')::jsonb,
                       pn."StartCommand", NULL, NULL, pn."OrderIndex"
                FROM "PenetrationNodes" pn
                LEFT JOIN "ImageTemplates" it ON it."Id" = pn."ImageTemplateId";

                INSERT INTO "TeamLabTopologyInterfaces"
                    ("Id", "AssetId", "NetworkId", "Key", "HostOffset", "IsPrimary", "OrderIndex")
                SELECT pi."Id", pi."NodeId", pi."NetworkId",
                       CASE
                           WHEN regexp_replace(lower(pi."Name"), '[^a-z0-9-]', '', 'g') ~ '^[a-z]'
                               THEN left(regexp_replace(lower(pi."Name"), '[^a-z0-9-]', '', 'g'), 63)
                           ELSE 'eth-' || pi."Id"::text
                       END,
                       CASE WHEN COALESCE(pi."StaticIp", '') ~ '^([0-9]{1,3}\.){3}[0-9]{1,3}$'
                           THEN GREATEST(3, split_part(pi."StaticIp", '.', 4)::int)
                           ELSE 3 + pi."OrderIndex" END,
                       pi."IsPrimary", pi."OrderIndex"
                FROM "PenetrationInterfaces" pi;

                INSERT INTO "TeamLabTopologyInterfaces"
                    ("AssetId", "NetworkId", "Key", "HostOffset", "IsPrimary", "OrderIndex")
                SELECT pn."Id", pn."NetworkId", 'eth0',
                       CASE WHEN COALESCE(pn."StaticIp", '') ~ '^([0-9]{1,3}\.){3}[0-9]{1,3}$'
                           THEN GREATEST(3, split_part(pn."StaticIp", '.', 4)::int)
                           ELSE 3 END,
                       true, 0
                FROM "PenetrationNodes" pn
                WHERE NOT EXISTS (
                    SELECT 1 FROM "PenetrationInterfaces" pi WHERE pi."NodeId" = pn."Id"
                );

                WITH edge_networks AS (
                    SELECT pe."Id", pe."ConfigId", pe."TopologyKey",
                           CASE WHEN pe."SourceKind" = 'Network'
                               THEN (SELECT n."TopologyKey" FROM "PenetrationNetworks" n WHERE n."Id" = pe."SourceId")
                               ELSE (SELECT n."TopologyKey" FROM "PenetrationNodes" a
                                     JOIN "PenetrationNetworks" n ON n."Id" = a."NetworkId"
                                     WHERE a."Id" = pe."SourceId") END AS from_key,
                           CASE WHEN pe."TargetKind" = 'Network'
                               THEN (SELECT n."TopologyKey" FROM "PenetrationNetworks" n WHERE n."Id" = pe."TargetId")
                               ELSE (SELECT n."TopologyKey" FROM "PenetrationNodes" a
                                     JOIN "PenetrationNetworks" n ON n."Id" = a."NetworkId"
                                     WHERE a."Id" = pe."TargetId") END AS to_key,
                           COALESCE(
                               (SELECT a."TopologyKey" FROM "PenetrationNodes" a
                                WHERE a."Id" = pe."SourceNodeId" AND a."AllowRouting"),
                               (SELECT a."TopologyKey" FROM "PenetrationNodes" a
                                WHERE a."Id" = pe."TargetNodeId" AND a."AllowRouting")) AS via_key
                    FROM "PenetrationEdges" pe
                    WHERE pe."PolicyAction" = 'Allow'
                      AND pe."EnforcementMode" IN ('RuntimeRoute', 'Both')
                )
                INSERT INTO "TeamLabTopologyConnections"
                    ("Id", "TopologyId", "Key", "FromNetworkKey", "ToNetworkKey", "ViaAssetKey")
                SELECT "Id", "ConfigId",
                       CASE WHEN "TopologyKey" ~ '^[a-z][a-z0-9-]{0,62}$' THEN "TopologyKey" ELSE 'connection-' || "Id"::text END,
                       from_key, to_key, via_key
                FROM edge_networks
                WHERE from_key IS NOT NULL AND to_key IS NOT NULL AND via_key IS NOT NULL AND from_key <> to_key;

                WITH objective_rows AS (
                    SELECT psi.*, pn."ConfigId", pn."TopologyKey" AS asset_key,
                           pc."GameId",
                           count(*) OVER (PARTITION BY pc."GameId", psi."TopologyKey") AS key_count
                    FROM "PenetrationScoreItems" psi
                    JOIN "PenetrationNodes" pn ON pn."Id" = psi."NodeId"
                    JOIN "PenetrationConfigs" pc ON pc."Id" = pn."ConfigId"
                )
                INSERT INTO "PenetrationObjectives"
                    ("Id", "GameId", "TopologyAssetKey", "Key", "Title", "Description", "Category", "Score",
                     "IsDynamic", "StaticFlag", "FlagTemplate", "MaxAttempts", "IsVisible", "IsCheckpoint",
                     "PrerequisiteObjectiveKeysJson", "OrderIndex")
                SELECT row."Id", row."GameId", row.asset_key,
                       CASE WHEN row.key_count = 1 AND row."TopologyKey" ~ '^[a-z][a-z0-9-]{0,62}$'
                           THEN row."TopologyKey" ELSE 'objective-' || row."Id"::text END,
                       row."Title", row."Description", row."Category", row."Score", row."IsDynamic",
                       row."StaticFlag", row."FlagTemplate", row."MaxAttempts", row."IsVisible", row."IsCheckpoint",
                       COALESCE((
                           SELECT jsonb_agg(
                               CASE WHEN dep_count.cnt = 1 AND dep."TopologyKey" ~ '^[a-z][a-z0-9-]{0,62}$'
                                   THEN dep."TopologyKey" ELSE 'objective-' || dep."Id"::text END
                               ORDER BY dep."Id")
                           FROM jsonb_array_elements_text(COALESCE(NULLIF(row."PrerequisiteItemIds", ''), '[]')::jsonb) value
                           JOIN "PenetrationScoreItems" dep ON dep."Id" = value::text::int
                           JOIN "PenetrationNodes" dep_node ON dep_node."Id" = dep."NodeId"
                           JOIN "PenetrationConfigs" dep_config ON dep_config."Id" = dep_node."ConfigId"
                           CROSS JOIN LATERAL (
                               SELECT count(*) AS cnt
                               FROM "PenetrationScoreItems" same_item
                               JOIN "PenetrationNodes" same_node ON same_node."Id" = same_item."NodeId"
                               JOIN "PenetrationConfigs" same_config ON same_config."Id" = same_node."ConfigId"
                               WHERE same_config."GameId" = dep_config."GameId"
                                 AND same_item."TopologyKey" = dep."TopologyKey"
                           ) dep_count
                       ), '[]'::jsonb), row."OrderIndex"
                FROM objective_rows row;

                INSERT INTO "PenetrationGameLabBindings"
                    ("GameId", "TopologyId", "ActiveReleaseId", "MaxResetCount", "CreatedAt", "UpdatedAt")
                SELECT pc."GameId", pc."Id", NULL, pc."MaxResetCount",
                       COALESCE(pc."PublishedAt", pc."UpdatedAt"), pc."UpdatedAt"
                FROM "PenetrationConfigs" pc;

                WITH release_payload AS (
                    SELECT t."Id" AS topology_id, t."SchemaVersion" AS schema_version, pc."PublishedVersion",
                           ps."CreatedBy", COALESCE(ps."CreatedAt", pc."PublishedAt", pc."UpdatedAt") AS published_at,
                           jsonb_build_object(
                               'name', t."Name",
                               'networks', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                                   'key', n."Key", 'name', n."Name",
                                   'addressPool', jsonb_build_object('poolCidr', n."AddressPoolCidr", 'runtimePrefixLength', n."RuntimePrefixLength"),
                                   'isEntry', n."IsEntry", 'orderIndex', n."OrderIndex") ORDER BY n."Key")
                                   FROM "TeamLabTopologyNetworks" n WHERE n."TopologyId" = t."Id"), '[]'::jsonb),
                               'assets', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                                   'key', a."Key", 'name', a."Name", 'kind', CASE WHEN a."Kind" = 0 THEN 'Docker' ELSE 'Vm' END,
                                   'imageTemplateId', a."ImageTemplateId",
                                   'resources', jsonb_build_object('cpuUnits', a."CpuUnits", 'memoryMiB', a."MemoryMiB", 'storageMiB', a."StorageMiB"),
                                   'interfaces', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                                       'key', i."Key", 'networkKey', n2."Key", 'hostOffset', i."HostOffset",
                                       'primary', i."IsPrimary", 'orderIndex', i."OrderIndex") ORDER BY i."Key")
                                       FROM "TeamLabTopologyInterfaces" i
                                       JOIN "TeamLabTopologyNetworks" n2 ON n2."Id" = i."NetworkId"
                                       WHERE i."AssetId" = a."Id"), '[]'::jsonb),
                                   'routingEnabled', a."RoutingEnabled", 'exposePort', a."ExposePort",
                                   'environment', a."EnvironmentJson", 'startCommand', a."StartCommand",
                                   'healthCheck', NULL, 'orderIndex', a."OrderIndex") ORDER BY a."Key")
                                   FROM "TeamLabTopologyAssets" a WHERE a."TopologyId" = t."Id"), '[]'::jsonb),
                               'connections', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                                   'key', c."Key", 'fromNetworkKey', c."FromNetworkKey", 'toNetworkKey', c."ToNetworkKey",
                                   'viaAssetKey', c."ViaAssetKey") ORDER BY c."Key")
                                   FROM "TeamLabTopologyConnections" c WHERE c."TopologyId" = t."Id"), '[]'::jsonb)
                           ) AS canonical
                    FROM "TeamLabTopologies" t
                    JOIN "PenetrationConfigs" pc ON pc."Id" = t."Id"
                    LEFT JOIN "PenetrationPublishedSnapshots" ps
                      ON ps."GameId" = pc."GameId" AND ps."PublishedVersion" = pc."PublishedVersion"
                    WHERE pc."PublishedVersion" > 0
                ), inserted_releases AS (
                    INSERT INTO "TeamLabTopologyReleases"
                        ("Id", "TopologyId", "Version", "SourceRevision", "SchemaVersion", "CanonicalJson",
                         "ContentHash", "PublishedById", "PublishedAt")
                    SELECT gen_random_uuid(), topology_id, "PublishedVersion", 1, schema_version, canonical,
                           'sha256:' || encode(digest(convert_to(
                               jsonb_build_object('schemaVersion', schema_version, 'topology', canonical)::text, 'UTF8'), 'sha256'), 'hex'),
                           "CreatedBy", published_at
                    FROM release_payload
                    RETURNING "Id", "TopologyId"
                )
                UPDATE "PenetrationGameLabBindings" binding
                SET "ActiveReleaseId" = release."Id"
                FROM inserted_releases release
                WHERE binding."TopologyId" = release."TopologyId";

                UPDATE "TeamLabRuntimes" runtime
                SET "TopologyReleaseId" = binding."ActiveReleaseId",
                    "CreateRequestHash" = encode(digest(convert_to(
                        runtime."GameId"::text || ':' || runtime."TeamId"::text || ':' || runtime."PublishedVersion"::text,
                        'UTF8'), 'sha256'), 'hex')
                FROM "PenetrationGameLabBindings" binding
                WHERE binding."GameId" = runtime."GameId";

                INSERT INTO "PenetrationTeamRuntimeBindings" ("GameId", "TeamId", "RuntimeId", "CreatedAt")
                SELECT "GameId", "TeamId", "Id", "CreatedAt" FROM "TeamLabRuntimes";

                UPDATE "TeamLabRuntimes" runtime
                SET "EntryShardId" = (
                    SELECT network."ShardId"
                    FROM "TeamLabRuntimeNetworks" network
                    JOIN "PenetrationGameLabBindings" binding ON binding."GameId" = runtime."GameId"
                    JOIN "TeamLabTopologyNetworks" topology_network
                      ON topology_network."TopologyId" = binding."TopologyId"
                     AND topology_network."Key" = network."TopologyKey"
                    WHERE network."RuntimeId" = runtime."Id" AND topology_network."IsEntry" AND network."ShardId" IS NOT NULL
                    ORDER BY network."Id" LIMIT 1
                )
                WHERE runtime."EntryShardId" IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM "TeamLabRuntimeNetworks" network
                    JOIN "PenetrationGameLabBindings" binding ON binding."GameId" = runtime."GameId"
                    JOIN "TeamLabTopologyNetworks" topology_network
                      ON topology_network."TopologyId" = binding."TopologyId"
                     AND topology_network."Key" = network."TopologyKey"
                    WHERE network."RuntimeId" = runtime."Id" AND topology_network."IsEntry" AND network."ShardId" IS NOT NULL
                  );

                INSERT INTO "TeamLabAccessGrants"
                    ("PublicId", "RuntimeId", "Generation", "Type", "ClientAddress", "Endpoint", "AllowedIps", "Dns",
                     "PublicKey", "ProtectedPrivateKey", "ServerPublicKey", "ProtectedServerPrivateKey", "Revoked",
                     "CreatedAt", "ExpiresAt", "RevokedAt")
                SELECT gen_random_uuid(), peer."RuntimeId", 1, 0, peer."ClientAddress", peer."Endpoint", peer."AllowedIPs",
                       peer."Dns", peer."PublicKey", peer."ProtectedClientPrivateKey", peer."ServerPublicKey",
                       peer."ProtectedServerPrivateKey", peer."Revoked", peer."CreatedAt", NULL,
                       CASE WHEN peer."Revoked" THEN peer."CreatedAt" ELSE NULL END
                FROM "TeamLabVpnPeerRuntimes" peer;

                INSERT INTO "TeamLabNetworkLeases"
                    ("RuntimeId", "Generation", "TopologyNetworkId", "AllocatedCidr", "AllocatedAt", "ReleasedAt")
                SELECT runtime_network."RuntimeId", 1, topology_network."Id", runtime_network."Cidr"::cidr,
                       runtime."CreatedAt", CASE WHEN runtime."Status" = 10 THEN COALESCE(runtime."UpdatedAt", now()) ELSE NULL END
                FROM "TeamLabRuntimeNetworks" runtime_network
                JOIN "TeamLabRuntimes" runtime ON runtime."Id" = runtime_network."RuntimeId"
                JOIN "PenetrationGameLabBindings" binding ON binding."GameId" = runtime."GameId"
                JOIN "TeamLabTopologyNetworks" topology_network
                  ON topology_network."TopologyId" = binding."TopologyId"
                 AND topology_network."Key" = runtime_network."TopologyKey"
                WHERE COALESCE(runtime_network."Cidr", '') <> '';

                UPDATE "TeamLabRuntimeNetworks" runtime_network
                SET "NetworkLeaseId" = lease."Id"
                FROM "TeamLabNetworkLeases" lease
                WHERE lease."RuntimeId" = runtime_network."RuntimeId"
                  AND lease."Generation" = runtime_network."Generation"
                  AND EXISTS (
                      SELECT 1 FROM "TeamLabTopologyNetworks" topology_network
                      WHERE topology_network."Id" = lease."TopologyNetworkId"
                        AND topology_network."Key" = runtime_network."TopologyKey"
                  );

                UPDATE "PenetrationSubmissions" submission
                SET "ObjectiveId" = objective."Id"
                FROM "PenetrationObjectives" objective
                WHERE objective."GameId" = submission."GameId"
                  AND objective."Id" = submission."ScoreItemId";

                SELECT setval(pg_get_serial_sequence('"TeamLabTopologies"', 'Id'), GREATEST(COALESCE((SELECT max("Id") FROM "TeamLabTopologies"), 1), 1), true);
                SELECT setval(pg_get_serial_sequence('"TeamLabTopologyNetworks"', 'Id'), GREATEST(COALESCE((SELECT max("Id") FROM "TeamLabTopologyNetworks"), 1), 1), true);
                SELECT setval(pg_get_serial_sequence('"TeamLabTopologyAssets"', 'Id'), GREATEST(COALESCE((SELECT max("Id") FROM "TeamLabTopologyAssets"), 1), 1), true);
                SELECT setval(pg_get_serial_sequence('"TeamLabTopologyInterfaces"', 'Id'), GREATEST(COALESCE((SELECT max("Id") FROM "TeamLabTopologyInterfaces"), 1), 1), true);
                SELECT setval(pg_get_serial_sequence('"TeamLabTopologyConnections"', 'Id'), GREATEST(COALESCE((SELECT max("Id") FROM "TeamLabTopologyConnections"), 1), 1), true);
                SELECT setval(pg_get_serial_sequence('"PenetrationObjectives"', 'Id'), GREATEST(COALESCE((SELECT max("Id") FROM "PenetrationObjectives"), 1), 1), true);

                ALTER TABLE "TeamLabNetworkLeases"
                ADD CONSTRAINT "EX_TeamLabNetworkLeases_ActiveCidr"
                EXCLUDE USING gist ("AllocatedCidr" inet_ops WITH &&)
                WHERE ("ReleasedAt" IS NULL);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeShards_RuntimeId_Generation_WorkerNodeId",
                table: "TeamLabRuntimeShards",
                columns: new[] { "RuntimeId", "Generation", "WorkerNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_CreatedById_ExternalReference",
                table: "TeamLabRuntimes",
                columns: new[] { "CreatedById", "ExternalReference" },
                unique: true,
                filter: "\"ExternalReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_EntryShardId",
                table: "TeamLabRuntimes",
                column: "EntryShardId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_PublicId",
                table: "TeamLabRuntimes",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_TopologyReleaseId",
                table: "TeamLabRuntimes",
                column: "TopologyReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeNetworks_NetworkLeaseId",
                table: "TeamLabRuntimeNetworks",
                column: "NetworkLeaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeNetworks_RuntimeId_Generation_TopologyKey",
                table: "TeamLabRuntimeNetworks",
                columns: new[] { "RuntimeId", "Generation", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_RuntimeId_Generation_Kind_TopologyKey",
                table: "TeamLabRuntimeAssets",
                columns: new[] { "RuntimeId", "Generation", "Kind", "TopologyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_ObjectiveId",
                table: "PenetrationSubmissions",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationGameLabBindings_ActiveReleaseId",
                table: "PenetrationGameLabBindings",
                column: "ActiveReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationGameLabBindings_TopologyId",
                table: "PenetrationGameLabBindings",
                column: "TopologyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationObjectives_GameId_Key",
                table: "PenetrationObjectives",
                columns: new[] { "GameId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationObjectives_GameId_TopologyAssetKey",
                table: "PenetrationObjectives",
                columns: new[] { "GameId", "TopologyAssetKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamRuntimeBindings_RuntimeId",
                table: "PenetrationTeamRuntimeBindings",
                column: "RuntimeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamRuntimeBindings_TeamId",
                table: "PenetrationTeamRuntimeBindings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabAccessGrants_PublicId",
                table: "TeamLabAccessGrants",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabAccessGrants_RuntimeId_Generation_Revoked",
                table: "TeamLabAccessGrants",
                columns: new[] { "RuntimeId", "Generation", "Revoked" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabNetworkLeases_ReleasedAt",
                table: "TeamLabNetworkLeases",
                column: "ReleasedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabNetworkLeases_RuntimeId_Generation_TopologyNetworkId",
                table: "TeamLabNetworkLeases",
                columns: new[] { "RuntimeId", "Generation", "TopologyNetworkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabNetworkLeases_TopologyNetworkId",
                table: "TeamLabNetworkLeases",
                column: "TopologyNetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeSecretEnvelopes_RuntimeId_Generation",
                table: "TeamLabRuntimeSecretEnvelopes",
                columns: new[] { "RuntimeId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologies_OwnerUserId",
                table: "TeamLabTopologies",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologies_PublicId",
                table: "TeamLabTopologies",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyAssets_ImageTemplateId",
                table: "TeamLabTopologyAssets",
                column: "ImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyAssets_TopologyId_Key",
                table: "TeamLabTopologyAssets",
                columns: new[] { "TopologyId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyConnections_TopologyId_Key",
                table: "TeamLabTopologyConnections",
                columns: new[] { "TopologyId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyInterfaces_AssetId_Key",
                table: "TeamLabTopologyInterfaces",
                columns: new[] { "AssetId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyInterfaces_NetworkId_AssetId",
                table: "TeamLabTopologyInterfaces",
                columns: new[] { "NetworkId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyNetworks_TopologyId_Key",
                table: "TeamLabTopologyNetworks",
                columns: new[] { "TopologyId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyReleases_PublishedById",
                table: "TeamLabTopologyReleases",
                column: "PublishedById");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyReleases_TopologyId_SourceRevision_ContentHa~",
                table: "TeamLabTopologyReleases",
                columns: new[] { "TopologyId", "SourceRevision", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyReleases_TopologyId_Version",
                table: "TeamLabTopologyReleases",
                columns: new[] { "TopologyId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationSubmissions_PenetrationObjectives_ObjectiveId",
                table: "PenetrationSubmissions",
                column: "ObjectiveId",
                principalTable: "PenetrationObjectives",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimeNetworks_TeamLabNetworkLeases_NetworkLeaseId",
                table: "TeamLabRuntimeNetworks",
                column: "NetworkLeaseId",
                principalTable: "TeamLabNetworkLeases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimes_AspNetUsers_CreatedById",
                table: "TeamLabRuntimes",
                column: "CreatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimes_TeamLabRuntimeShards_EntryShardId",
                table: "TeamLabRuntimes",
                column: "EntryShardId",
                principalTable: "TeamLabRuntimeShards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimes_TeamLabTopologyReleases_TopologyReleaseId",
                table: "TeamLabRuntimes",
                column: "TopologyReleaseId",
                principalTable: "TeamLabTopologyReleases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationSubmissions_PenetrationObjectives_ObjectiveId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimeNetworks_TeamLabNetworkLeases_NetworkLeaseId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimes_AspNetUsers_CreatedById",
                table: "TeamLabRuntimes");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimes_TeamLabRuntimeShards_EntryShardId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimes_TeamLabTopologyReleases_TopologyReleaseId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropTable(
                name: "PenetrationGameLabBindings");

            migrationBuilder.DropTable(
                name: "PenetrationObjectives");

            migrationBuilder.DropTable(
                name: "PenetrationTeamRuntimeBindings");

            migrationBuilder.DropTable(
                name: "TeamLabAccessGrants");

            migrationBuilder.DropTable(
                name: "TeamLabNetworkLeases");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeSecretEnvelopes");

            migrationBuilder.DropTable(
                name: "TeamLabTopologyConnections");

            migrationBuilder.DropTable(
                name: "TeamLabTopologyInterfaces");

            migrationBuilder.DropTable(
                name: "TeamLabTopologyReleases");

            migrationBuilder.DropTable(
                name: "TeamLabTopologyAssets");

            migrationBuilder.DropTable(
                name: "TeamLabTopologyNetworks");

            migrationBuilder.DropTable(
                name: "TeamLabTopologies");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeShards_RuntimeId_Generation_WorkerNodeId",
                table: "TeamLabRuntimeShards");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_CreatedById_ExternalReference",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_EntryShardId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_PublicId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_TopologyReleaseId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeNetworks_NetworkLeaseId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeNetworks_RuntimeId_Generation_TopologyKey",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimeAssets_RuntimeId_Generation_Kind_TopologyKey",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_ObjectiveId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "FirstSeenAt",
                table: "TeamLabTrafficFlows");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabTrafficFlows");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "TeamLabTrafficFlows");

            migrationBuilder.DropColumn(
                name: "Packets",
                table: "TeamLabTrafficFlows");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabRuntimeShards");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TeamLabRuntimeShards");

            migrationBuilder.DropColumn(
                name: "CreateRequestHash",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "EntryShardId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "TopologyReleaseId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropColumn(
                name: "NetworkLeaseId",
                table: "TeamLabRuntimeNetworks");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabRuntimeAssets");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabPublicUdpMappings");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "TeamLabEvents");

            migrationBuilder.DropColumn(
                name: "ObjectiveId",
                table: "PenetrationSubmissions");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeShards_RuntimeId_WorkerNodeId",
                table: "TeamLabRuntimeShards",
                columns: new[] { "RuntimeId", "WorkerNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeNetworks_RuntimeId_TopologyKey",
                table: "TeamLabRuntimeNetworks",
                columns: new[] { "RuntimeId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_RuntimeId_Kind_TopologyKey",
                table: "TeamLabRuntimeAssets",
                columns: new[] { "RuntimeId", "Kind", "TopologyKey" });
        }
    }
}
