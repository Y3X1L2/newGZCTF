using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class RemovePenetrationTopologyRuntimeCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "PenetrationTeamEnvironments" environment
                        LEFT JOIN "PenetrationTeamRuntimeBindings" binding
                          ON binding."GameId" = environment."GameId"
                         AND binding."TeamId" = environment."TeamId"
                        WHERE environment."Status" NOT IN ('Stopped', 'Failed')
                          AND binding."RuntimeId" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Cannot remove legacy penetration runtime: an active environment has no TeamLab runtime binding.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "PenetrationResetRecords" reset_record
                        JOIN "PenetrationTeamEnvironments" environment
                          ON environment."Id" = reset_record."EnvironmentId"
                        LEFT JOIN "PenetrationTeamRuntimeBindings" binding
                          ON binding."GameId" = environment."GameId"
                         AND binding."TeamId" = environment."TeamId"
                        WHERE binding."RuntimeId" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Cannot migrate penetration reset records: an environment has no TeamLab runtime binding.';
                    END IF;

                    IF EXISTS (SELECT 1 FROM "PenetrationSubmissions" WHERE "ObjectiveId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot remove legacy penetration objectives: a submission has no objective binding.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "TeamLabRuntimes" runtime
                        WHERE runtime."TopologyReleaseId" IS NULL
                           OR runtime."EntryShardId" IS NULL
                           OR NOT EXISTS (
                               SELECT 1 FROM "TeamLabRuntimeNetworks" network
                               WHERE network."RuntimeId" = runtime."Id"
                                 AND network."Generation" = runtime."Generation")
                           OR NOT EXISTS (
                               SELECT 1 FROM "TeamLabRuntimeAssets" asset
                               WHERE asset."RuntimeId" = runtime."Id"
                                 AND asset."Generation" = runtime."Generation")
                    ) THEN
                        RAISE EXCEPTION 'Cannot remove legacy TeamLab runtime columns: a runtime is missing release, entry shard, network, or asset facts.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationResetRecords_PenetrationTeamEnvironments_Environ~",
                table: "PenetrationResetRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationSubmissions_PenetrationScoreItems_ScoreItemId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimes_Games_GameId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimes_Teams_TeamId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabRuntimes_WorkerNodes_WorkerNodeId",
                table: "TeamLabRuntimes");

            migrationBuilder.Sql(
                """
                UPDATE "PenetrationResetRecords" reset_record
                SET "EnvironmentId" = binding."RuntimeId"
                FROM "PenetrationTeamEnvironments" environment
                JOIN "PenetrationTeamRuntimeBindings" binding
                  ON binding."GameId" = environment."GameId"
                 AND binding."TeamId" = environment."TeamId"
                WHERE reset_record."EnvironmentId" = environment."Id";
                """);

            migrationBuilder.DropTable(
                name: "PenetrationDeploymentEvents");

            migrationBuilder.DropTable(
                name: "PenetrationEdges");

            migrationBuilder.DropTable(
                name: "PenetrationInterfaces");

            migrationBuilder.DropTable(
                name: "PenetrationPublishedSnapshots");

            migrationBuilder.DropTable(
                name: "PenetrationRuntimeNodes");

            migrationBuilder.DropTable(
                name: "PenetrationRuntimeRoutes");

            migrationBuilder.DropTable(
                name: "PenetrationScoreItems");

            migrationBuilder.DropTable(
                name: "PenetrationTeamEnvironments");

            migrationBuilder.DropTable(
                name: "PenetrationNodes");

            migrationBuilder.DropTable(
                name: "PenetrationNetworks");

            migrationBuilder.DropTable(
                name: "PenetrationConfigs");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_GameId_TeamId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_TeamId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabRuntimes_WorkerNodeId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_PublishedVersion_ScoreItemTopologyKey",
                table: "PenetrationSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_ScoreItemId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_ScoreItemId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "NetworkPrefix",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "WorkerNodeId",
                table: "TeamLabRuntimes");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "ScoreItemId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "ScoreItemTopologyKey",
                table: "PenetrationSubmissions");

            migrationBuilder.RenameColumn(
                name: "EnvironmentId",
                table: "PenetrationResetRecords",
                newName: "RuntimeId");

            migrationBuilder.RenameIndex(
                name: "IX_PenetrationResetRecords_EnvironmentId",
                table: "PenetrationResetRecords",
                newName: "IX_PenetrationResetRecords_RuntimeId");

            migrationBuilder.AddColumn<string>(
                name: "EditorMetadataJson",
                table: "TeamLabTopologies",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"networks\":{},\"assets\":{}}");

            migrationBuilder.AlterColumn<Guid>(
                name: "TopologyReleaseId",
                table: "TeamLabRuntimes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfigurationConsumedAt",
                table: "TeamLabAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadTokenHash",
                table: "TeamLabAccessGrants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "TeamLabAccessGrants"
                SET "Revoked" = TRUE,
                    "RevokedAt" = COALESCE("RevokedAt", NOW())
                WHERE "DownloadTokenHash" = '';

                UPDATE "TeamLabRuntimes" runtime
                SET "IsOpenToPlayers" = FALSE
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "TeamLabAccessGrants" access_grant
                    WHERE access_grant."RuntimeId" = runtime."Id"
                      AND access_grant."Generation" = runtime."Generation"
                      AND access_grant."Revoked" = FALSE
                      AND access_grant."DownloadTokenHash" <> ''
                );
                """);

            migrationBuilder.AlterColumn<int>(
                name: "ObjectiveId",
                table: "PenetrationSubmissions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApiOperationId",
                table: "DeploymentQueueTickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceDisplayName",
                table: "DeploymentQueueTickets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectDisplayName",
                table: "DeploymentQueueTickets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectPublicId",
                table: "DeploymentQueueTickets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectType",
                table: "DeploymentQueueTickets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeOperationJobs",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    RuntimeId = table.Column<int>(type: "integer", nullable: true),
                    RuntimePublicId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeOperationJobs", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeOperationJobs_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeOperationJobs_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_ObjectiveId",
                table: "PenetrationSubmissions",
                columns: new[] { "GameId", "TeamId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeOperationJobs_RuntimeId",
                table: "TeamLabRuntimeOperationJobs",
                column: "RuntimeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeOperationJobs_RuntimePublicId",
                table: "TeamLabRuntimeOperationJobs",
                column: "RuntimePublicId");

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationResetRecords_TeamLabRuntimes_RuntimeId",
                table: "PenetrationResetRecords",
                column: "RuntimeId",
                principalTable: "TeamLabRuntimes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationResetRecords_TeamLabRuntimes_RuntimeId",
                table: "PenetrationResetRecords");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeOperationJobs");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_ObjectiveId",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "EditorMetadataJson",
                table: "TeamLabTopologies");

            migrationBuilder.DropColumn(
                name: "ConfigurationConsumedAt",
                table: "TeamLabAccessGrants");

            migrationBuilder.DropColumn(
                name: "DownloadTokenHash",
                table: "TeamLabAccessGrants");

            migrationBuilder.DropColumn(
                name: "ApiOperationId",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "ResourceDisplayName",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "SubjectDisplayName",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "SubjectPublicId",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "DeploymentQueueTickets");

            migrationBuilder.RenameColumn(
                name: "RuntimeId",
                table: "PenetrationResetRecords",
                newName: "EnvironmentId");

            migrationBuilder.RenameIndex(
                name: "IX_PenetrationResetRecords_RuntimeId",
                table: "PenetrationResetRecords",
                newName: "IX_PenetrationResetRecords_EnvironmentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "TopologyReleaseId",
                table: "TeamLabRuntimes",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "TeamLabRuntimes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NetworkPrefix",
                table: "TeamLabRuntimes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
                table: "TeamLabRuntimes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "TeamLabRuntimes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkerNodeId",
                table: "TeamLabRuntimes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ObjectiveId",
                table: "PenetrationSubmissions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
                table: "PenetrationSubmissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoreItemId",
                table: "PenetrationSubmissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScoreItemTopologyKey",
                table: "PenetrationSubmissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PenetrationConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    BaseCidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeployedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxResetCount = table.Column<int>(type: "integer", nullable: false),
                    NetworkSubnetPrefix = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TeamSubnetPrefix = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationConfigs_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationPublishedSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationPublishedSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationPublishedSnapshots_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationTeamEnvironments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    CleanupRetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastCleanupAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    NetworkPrefix = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NextCleanupAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    ResetCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TeamIndex = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationTeamEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamEnvironments_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamEnvironments_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationTeamEnvironments_WorkerNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PenetrationEdges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConfigId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EnforcementMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "HintOnly"),
                    IsRouteHint = table.Column<bool>(type: "boolean", nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PolicyAction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PortRange = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    Protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceNodeId = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetNodeId = table.Column<int>(type: "integer", nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationEdges_PenetrationConfigs_ConfigId",
                        column: x => x.ConfigId,
                        principalTable: "PenetrationConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationNetworks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConfigId = table.Column<int>(type: "integer", nullable: false),
                    Cidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Collapsed = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Height = table.Column<double>(type: "double precision", nullable: false),
                    IsEntry = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TrustLevel = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<double>(type: "double precision", nullable: false),
                    ZoneType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationNetworks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationNetworks_PenetrationConfigs_ConfigId",
                        column: x => x.ConfigId,
                        principalTable: "PenetrationConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationDeploymentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NodeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationDeploymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationDeploymentEvents_PenetrationTeamEnvironments_Env~",
                        column: x => x.EnvironmentId,
                        principalTable: "PenetrationTeamEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationRuntimeRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CommandSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EdgeTopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnforcementMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GatewayIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RouteNodeKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RouteNodeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceCidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceNetworkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetCidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetNetworkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationRuntimeRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationRuntimeRoutes_PenetrationTeamEnvironments_Enviro~",
                        column: x => x.EnvironmentId,
                        principalTable: "PenetrationTeamEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConfigId = table.Column<int>(type: "integer", nullable: false),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    NetworkId = table.Column<int>(type: "integer", nullable: false),
                    AllowRouting = table.Column<bool>(type: "boolean", nullable: false),
                    CpuCount = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EnvironmentVariables = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ExposePort = table.Column<int>(type: "integer", nullable: false),
                    HealthCheck = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ImageName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsEntry = table.Column<bool>(type: "boolean", nullable: false),
                    MemoryLimit = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NodeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    PlayerAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PlayerDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    PublishPort = table.Column<bool>(type: "boolean", nullable: false),
                    ReservedAdRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StartCommand = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StaticIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StorageLimit = table.Column<int>(type: "integer", nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationNodes_ImageTemplates_ImageTemplateId",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenetrationNodes_PenetrationConfigs_ConfigId",
                        column: x => x.ConfigId,
                        principalTable: "PenetrationConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationNodes_PenetrationNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "PenetrationNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationInterfaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NetworkId = table.Column<int>(type: "integer", nullable: false),
                    NodeId = table.Column<int>(type: "integer", nullable: false),
                    IsManagement = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    StaticIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationInterfaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationInterfaces_PenetrationNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "PenetrationNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationInterfaces_PenetrationNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "PenetrationNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationRuntimeNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContainerId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                    TopologyNodeId = table.Column<int>(type: "integer", nullable: false),
                    AdminAccessUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InterfaceSummary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NetworkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublicPort = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TopologyNodeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationRuntimeNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationRuntimeNodes_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PenetrationRuntimeNodes_PenetrationNodes_TopologyNodeId",
                        column: x => x.TopologyNodeId,
                        principalTable: "PenetrationNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationRuntimeNodes_PenetrationTeamEnvironments_Environ~",
                        column: x => x.EnvironmentId,
                        principalTable: "PenetrationTeamEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationScoreItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NodeId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FlagTemplate = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IsCheckpoint = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsDynamic = table.Column<bool>(type: "boolean", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    PrerequisiteItemIds = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    StaticFlag = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: true),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationScoreItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationScoreItems_PenetrationNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "PenetrationNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_GameId_TeamId",
                table: "TeamLabRuntimes",
                columns: new[] { "GameId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_TeamId",
                table: "TeamLabRuntimes",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimes_WorkerNodeId",
                table: "TeamLabRuntimes",
                column: "WorkerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_PublishedVersion_ScoreItemTopologyKey",
                table: "PenetrationSubmissions",
                columns: new[] { "GameId", "TeamId", "PublishedVersion", "ScoreItemTopologyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_ScoreItemId",
                table: "PenetrationSubmissions",
                columns: new[] { "GameId", "TeamId", "ScoreItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_ScoreItemId",
                table: "PenetrationSubmissions",
                column: "ScoreItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationConfigs_GameId",
                table: "PenetrationConfigs",
                column: "GameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationDeploymentEvents_EnvironmentId_CreatedAt",
                table: "PenetrationDeploymentEvents",
                columns: new[] { "EnvironmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationEdges_ConfigId",
                table: "PenetrationEdges",
                column: "ConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationEdges_ConfigId_TopologyKey",
                table: "PenetrationEdges",
                columns: new[] { "ConfigId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationInterfaces_NetworkId",
                table: "PenetrationInterfaces",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationInterfaces_NodeId",
                table: "PenetrationInterfaces",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationInterfaces_NodeId_TopologyKey",
                table: "PenetrationInterfaces",
                columns: new[] { "NodeId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNetworks_ConfigId",
                table: "PenetrationNetworks",
                column: "ConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNetworks_ConfigId_TopologyKey",
                table: "PenetrationNetworks",
                columns: new[] { "ConfigId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_ConfigId",
                table: "PenetrationNodes",
                column: "ConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_ConfigId_TopologyKey",
                table: "PenetrationNodes",
                columns: new[] { "ConfigId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_ImageTemplateId",
                table: "PenetrationNodes",
                column: "ImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_NetworkId",
                table: "PenetrationNodes",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationPublishedSnapshots_GameId_PublishedVersion",
                table: "PenetrationPublishedSnapshots",
                columns: new[] { "GameId", "PublishedVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationRuntimeNodes_ContainerId",
                table: "PenetrationRuntimeNodes",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationRuntimeNodes_EnvironmentId",
                table: "PenetrationRuntimeNodes",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationRuntimeNodes_TopologyNodeId",
                table: "PenetrationRuntimeNodes",
                column: "TopologyNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationRuntimeRoutes_EdgeTopologyKey",
                table: "PenetrationRuntimeRoutes",
                column: "EdgeTopologyKey");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationRuntimeRoutes_EnvironmentId",
                table: "PenetrationRuntimeRoutes",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationScoreItems_NodeId",
                table: "PenetrationScoreItems",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationScoreItems_NodeId_TopologyKey",
                table: "PenetrationScoreItems",
                columns: new[] { "NodeId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamEnvironments_GameId_TeamId",
                table: "PenetrationTeamEnvironments",
                columns: new[] { "GameId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamEnvironments_NodeId",
                table: "PenetrationTeamEnvironments",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamEnvironments_TeamId",
                table: "PenetrationTeamEnvironments",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationResetRecords_PenetrationTeamEnvironments_Environ~",
                table: "PenetrationResetRecords",
                column: "EnvironmentId",
                principalTable: "PenetrationTeamEnvironments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationSubmissions_PenetrationScoreItems_ScoreItemId",
                table: "PenetrationSubmissions",
                column: "ScoreItemId",
                principalTable: "PenetrationScoreItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimes_Games_GameId",
                table: "TeamLabRuntimes",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimes_Teams_TeamId",
                table: "TeamLabRuntimes",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabRuntimes_WorkerNodes_WorkerNodeId",
                table: "TeamLabRuntimes",
                column: "WorkerNodeId",
                principalTable: "WorkerNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
