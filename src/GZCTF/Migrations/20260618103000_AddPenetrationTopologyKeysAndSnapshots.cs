using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddPenetrationTopologyKeysAndSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TopologyKey",
                table: "PenetrationScoreItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopologyNodeKey",
                table: "PenetrationRuntimeNodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopologyKey",
                table: "PenetrationNodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopologyKey",
                table: "PenetrationNetworks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopologyKey",
                table: "PenetrationInterfaces",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TopologyKey",
                table: "PenetrationEdges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
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

            migrationBuilder.Sql("""
                UPDATE "PenetrationNetworks"
                SET "TopologyKey" = 'legacy-network-' || "Id"
                WHERE "TopologyKey" = '';

                UPDATE "PenetrationNodes"
                SET "TopologyKey" = 'legacy-node-' || "Id"
                WHERE "TopologyKey" = '';

                UPDATE "PenetrationInterfaces"
                SET "TopologyKey" = 'legacy-interface-' || "Id"
                WHERE "TopologyKey" = '';

                UPDATE "PenetrationEdges"
                SET "TopologyKey" = 'legacy-edge-' || "Id"
                WHERE "TopologyKey" = '';

                UPDATE "PenetrationScoreItems"
                SET "TopologyKey" = 'legacy-score-' || "Id"
                WHERE "TopologyKey" = '';

                UPDATE "PenetrationRuntimeNodes" AS runtime
                SET "TopologyNodeKey" = node."TopologyKey"
                FROM "PenetrationNodes" AS node
                WHERE runtime."TopologyNodeId" = node."Id"
                  AND runtime."TopologyNodeKey" = '';

                UPDATE "PenetrationSubmissions" AS submission
                SET "ScoreItemTopologyKey" = item."TopologyKey"
                FROM "PenetrationScoreItems" AS item
                WHERE submission."ScoreItemId" = item."Id"
                  AND submission."ScoreItemTopologyKey" = '';

                UPDATE "PenetrationSubmissions" AS submission
                SET "PublishedVersion" = env."PublishedVersion"
                FROM "PenetrationTeamEnvironments" AS env
                WHERE submission."GameId" = env."GameId"
                  AND submission."TeamId" = env."TeamId"
                  AND submission."PublishedVersion" = 0;
                """);

            migrationBuilder.CreateTable(
                name: "PenetrationPublishedSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationScoreItems_NodeId_TopologyKey",
                table: "PenetrationScoreItems",
                columns: new[] { "NodeId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNodes_ConfigId_TopologyKey",
                table: "PenetrationNodes",
                columns: new[] { "ConfigId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationNetworks_ConfigId_TopologyKey",
                table: "PenetrationNetworks",
                columns: new[] { "ConfigId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationInterfaces_NodeId_TopologyKey",
                table: "PenetrationInterfaces",
                columns: new[] { "NodeId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationEdges_ConfigId_TopologyKey",
                table: "PenetrationEdges",
                columns: new[] { "ConfigId", "TopologyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationPublishedSnapshots_GameId_PublishedVersion",
                table: "PenetrationPublishedSnapshots",
                columns: new[] { "GameId", "PublishedVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_PublishedVersion_ScoreItemTopologyKey",
                table: "PenetrationSubmissions",
                columns: new[] { "GameId", "TeamId", "PublishedVersion", "ScoreItemTopologyKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenetrationPublishedSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationScoreItems_NodeId_TopologyKey",
                table: "PenetrationScoreItems");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationNodes_ConfigId_TopologyKey",
                table: "PenetrationNodes");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationNetworks_ConfigId_TopologyKey",
                table: "PenetrationNetworks");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationInterfaces_NodeId_TopologyKey",
                table: "PenetrationInterfaces");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationEdges_ConfigId_TopologyKey",
                table: "PenetrationEdges");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationSubmissions_GameId_TeamId_PublishedVersion_ScoreItemTopologyKey",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "ScoreItemTopologyKey",
                table: "PenetrationSubmissions");

            migrationBuilder.DropColumn(
                name: "TopologyKey",
                table: "PenetrationScoreItems");

            migrationBuilder.DropColumn(
                name: "TopologyNodeKey",
                table: "PenetrationRuntimeNodes");

            migrationBuilder.DropColumn(
                name: "TopologyKey",
                table: "PenetrationNodes");

            migrationBuilder.DropColumn(
                name: "TopologyKey",
                table: "PenetrationNetworks");

            migrationBuilder.DropColumn(
                name: "TopologyKey",
                table: "PenetrationInterfaces");

            migrationBuilder.DropColumn(
                name: "TopologyKey",
                table: "PenetrationEdges");
        }
    }
}
