using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabNetworkControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLabRuntimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    WorkerNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    NetworkPrefix = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    IsOpenToPlayers = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimes_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimes_WorkerNodes_WorkerNodeId",
                        column: x => x.WorkerNodeId,
                        principalTable: "WorkerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Level = table.Column<byte>(type: "smallint", nullable: false),
                    Message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ObjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabEvents_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabPublicUdpMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    PublicUdpPort = table.Column<int>(type: "integer", nullable: false),
                    WorkerTunnelIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkerWireGuardPort = table.Column<int>(type: "integer", nullable: false),
                    RuleVersion = table.Column<int>(type: "integer", nullable: false),
                    IsSynced = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabPublicUdpMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabPublicUdpMappings_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RuntimeResourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeAssets_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabRuntimeNetworks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    TopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Cidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GatewayIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BridgeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabRuntimeNetworks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabRuntimeNetworks_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabVpnPeerRuntimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuntimeId = table.Column<int>(type: "integer", nullable: false),
                    ClientAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AllowedIPs = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Dns = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConfigVersion = table.Column<int>(type: "integer", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabVpnPeerRuntimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabVpnPeerRuntimes_TeamLabRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "TeamLabRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabEvents_RuntimeId_CreatedAt",
                table: "TeamLabEvents",
                columns: new[] { "RuntimeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabPublicUdpMappings_PublicUdpPort",
                table: "TeamLabPublicUdpMappings",
                column: "PublicUdpPort",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabPublicUdpMappings_RuntimeId",
                table: "TeamLabPublicUdpMappings",
                column: "RuntimeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeAssets_RuntimeId_Kind_TopologyKey",
                table: "TeamLabRuntimeAssets",
                columns: new[] { "RuntimeId", "Kind", "TopologyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRuntimeNetworks_RuntimeId_TopologyKey",
                table: "TeamLabRuntimeNetworks",
                columns: new[] { "RuntimeId", "TopologyKey" },
                unique: true);

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
                name: "IX_TeamLabVpnPeerRuntimes_RuntimeId_Revoked",
                table: "TeamLabVpnPeerRuntimes",
                columns: new[] { "RuntimeId", "Revoked" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLabEvents");

            migrationBuilder.DropTable(
                name: "TeamLabPublicUdpMappings");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeAssets");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimeNetworks");

            migrationBuilder.DropTable(
                name: "TeamLabVpnPeerRuntimes");

            migrationBuilder.DropTable(
                name: "TeamLabRuntimes");
        }
    }
}
