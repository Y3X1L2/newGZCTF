using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class PenetrationLowCodeOrchestrationUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminAccessUrl",
                table: "PenetrationRuntimeNodes",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterfaceSummary",
                table: "PenetrationRuntimeNodes",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PenetrationNodes",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Collapsed",
                table: "PenetrationNetworks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPolicy",
                table: "PenetrationNetworks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "DenyAll");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PenetrationNetworks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrustLevel",
                table: "PenetrationNetworks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ZoneType",
                table: "PenetrationNetworks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Custom");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PenetrationEdges",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRouteHint",
                table: "PenetrationEdges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PolicyAction",
                table: "PenetrationEdges",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Allow");

            migrationBuilder.AddColumn<string>(
                name: "PortRange",
                table: "PenetrationEdges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Protocol",
                table: "PenetrationEdges",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Tcp");

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "PenetrationEdges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "PenetrationEdges",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Node");

            migrationBuilder.AddColumn<int>(
                name: "TargetId",
                table: "PenetrationEdges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TargetKind",
                table: "PenetrationEdges",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Node");

            migrationBuilder.CreateTable(
                name: "PenetrationInterfaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NodeId = table.Column<int>(type: "integer", nullable: false),
                    NetworkId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StaticIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsManagement = table.Column<bool>(type: "boolean", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationInterfaces_NetworkId",
                table: "PenetrationInterfaces",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationInterfaces_NodeId",
                table: "PenetrationInterfaces",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenetrationInterfaces");

            migrationBuilder.DropColumn(
                name: "AdminAccessUrl",
                table: "PenetrationRuntimeNodes");

            migrationBuilder.DropColumn(
                name: "InterfaceSummary",
                table: "PenetrationRuntimeNodes");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "PenetrationNodes");

            migrationBuilder.DropColumn(
                name: "Collapsed",
                table: "PenetrationNetworks");

            migrationBuilder.DropColumn(
                name: "DefaultPolicy",
                table: "PenetrationNetworks");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "PenetrationNetworks");

            migrationBuilder.DropColumn(
                name: "TrustLevel",
                table: "PenetrationNetworks");

            migrationBuilder.DropColumn(
                name: "ZoneType",
                table: "PenetrationNetworks");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "IsRouteHint",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "PolicyAction",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "PortRange",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "Protocol",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "PenetrationEdges");

            migrationBuilder.DropColumn(
                name: "TargetKind",
                table: "PenetrationEdges");
        }
    }
}
