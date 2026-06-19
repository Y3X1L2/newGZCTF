using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddPenetrationRuntimeRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowRouting",
                table: "PenetrationNodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EnforcementMode",
                table: "PenetrationEdges",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "HintOnly");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "PenetrationEdges",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.Sql("""
                UPDATE "PenetrationNodes"
                SET "AllowRouting" = TRUE
                WHERE "NodeType" IN ('JumpHost', 'Bastion', 'FirewallRouter');
                """);

            migrationBuilder.CreateTable(
                name: "PenetrationRuntimeRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                    EdgeTopologyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnforcementMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RouteNodeKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RouteNodeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceNetworkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TargetNetworkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceCidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetCidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GatewayIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CommandSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationRuntimeRoutes_EdgeTopologyKey",
                table: "PenetrationRuntimeRoutes",
                column: "EdgeTopologyKey");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationRuntimeRoutes_EnvironmentId",
                table: "PenetrationRuntimeRoutes",
                column: "EnvironmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenetrationRuntimeRoutes");

            migrationBuilder.DropColumn(
                name: "AllowRouting",
                table: "PenetrationNodes");

            migrationBuilder.DropColumn(
                name: "EnforcementMode",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "PenetrationEdges");
        }
    }
}
