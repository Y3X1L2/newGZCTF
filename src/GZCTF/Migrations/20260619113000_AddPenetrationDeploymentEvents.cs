using System;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260619113000_AddPenetrationDeploymentEvents")]
    public partial class AddPenetrationDeploymentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PenetrationDeploymentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NodeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationDeploymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenetrationDeploymentEvents_PenetrationTeamEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "PenetrationTeamEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationDeploymentEvents_EnvironmentId_CreatedAt",
                table: "PenetrationDeploymentEvents",
                columns: new[] { "EnvironmentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PenetrationDeploymentEvents");
        }
    }
}
