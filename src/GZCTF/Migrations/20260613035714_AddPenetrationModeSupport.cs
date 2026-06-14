using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddPenetrationModeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PenetrationConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    BaseCidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TeamSubnetPrefix = table.Column<int>(type: "integer", nullable: false),
                    NetworkSubnetPrefix = table.Column<int>(type: "integer", nullable: false),
                    MaxResetCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeployedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "PenetrationTeamEnvironments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    NetworkPrefix = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResetCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
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
                    SourceNodeId = table.Column<int>(type: "integer", nullable: false),
                    TargetNodeId = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
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
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Cidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    IsEntry = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "PenetrationResetRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    ResetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationResetRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationResetRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PenetrationResetRecords_PenetrationTeamEnvironments_Environ~",
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
                    NetworkId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NodeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ImageTemplateId = table.Column<int>(type: "integer", nullable: true),
                    ImageName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CpuCount = table.Column<int>(type: "integer", nullable: false),
                    MemoryLimit = table.Column<int>(type: "integer", nullable: false),
                    StorageLimit = table.Column<int>(type: "integer", nullable: false),
                    ExposePort = table.Column<int>(type: "integer", nullable: false),
                    IsEntry = table.Column<bool>(type: "boolean", nullable: false),
                    PublishPort = table.Column<bool>(type: "boolean", nullable: false),
                    StaticIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EnvironmentVariables = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    StartCommand = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    HealthCheck = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReservedAdRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationNodes_ImageTemplates_ImageTemplateId",
                        column: x => x.ImageTemplateId,
                        principalTable: "ImageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "PenetrationRuntimeNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                    TopologyNodeId = table.Column<int>(type: "integer", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uuid", nullable: true),
                    NetworkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicPort = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    IsDynamic = table.Column<bool>(type: "boolean", nullable: false),
                    StaticFlag = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: true),
                    FlagTemplate = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    PrerequisiteItemIds = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "PenetrationSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ParticipationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScoreItemId = table.Column<int>(type: "integer", nullable: false),
                    Answer = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationSubmissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationSubmissions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationSubmissions_Participations_ParticipationId",
                        column: x => x.ParticipationId,
                        principalTable: "Participations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationSubmissions_PenetrationScoreItems_ScoreItemId",
                        column: x => x.ScoreItemId,
                        principalTable: "PenetrationScoreItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PenetrationSubmissions_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationConfigs_GameId",
                table: "PenetrationConfigs",
                column: "GameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationEdges_ConfigId",
                table: "PenetrationEdges",
                column: "ConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNetworks_ConfigId",
                table: "PenetrationNetworks",
                column: "ConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_ConfigId",
                table: "PenetrationNodes",
                column: "ConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_ImageTemplateId",
                table: "PenetrationNodes",
                column: "ImageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_NetworkId",
                table: "PenetrationNodes",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationResetRecords_EnvironmentId",
                table: "PenetrationResetRecords",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationResetRecords_UserId",
                table: "PenetrationResetRecords",
                column: "UserId");

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
                name: "IX_PenetrationScoreItems_NodeId",
                table: "PenetrationScoreItems",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_ScoreItemId",
                table: "PenetrationSubmissions",
                columns: new[] { "GameId", "TeamId", "ScoreItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_ParticipationId",
                table: "PenetrationSubmissions",
                column: "ParticipationId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_ScoreItemId",
                table: "PenetrationSubmissions",
                column: "ScoreItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_TeamId",
                table: "PenetrationSubmissions",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_UserId",
                table: "PenetrationSubmissions",
                column: "UserId");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenetrationEdges");

            migrationBuilder.DropTable(
                name: "PenetrationResetRecords");

            migrationBuilder.DropTable(
                name: "PenetrationRuntimeNodes");

            migrationBuilder.DropTable(
                name: "PenetrationSubmissions");

            migrationBuilder.DropTable(
                name: "PenetrationTeamEnvironments");

            migrationBuilder.DropTable(
                name: "PenetrationScoreItems");

            migrationBuilder.DropTable(
                name: "PenetrationNodes");

            migrationBuilder.DropTable(
                name: "PenetrationNetworks");

            migrationBuilder.DropTable(
                name: "PenetrationConfigs");
        }
    }
}
