using System;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260619093000_AddPenetrationDeploymentCleanupState")]
    public partial class AddPenetrationDeploymentCleanupState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CleanupRetryCount",
                table: "PenetrationTeamEnvironments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCleanupAttemptAt",
                table: "PenetrationTeamEnvironments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextCleanupAt",
                table: "PenetrationTeamEnvironments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CleanupRetryCount",
                table: "PenetrationTeamEnvironments");

            migrationBuilder.DropColumn(
                name: "LastCleanupAttemptAt",
                table: "PenetrationTeamEnvironments");

            migrationBuilder.DropColumn(
                name: "NextCleanupAt",
                table: "PenetrationTeamEnvironments");
        }
    }
}
