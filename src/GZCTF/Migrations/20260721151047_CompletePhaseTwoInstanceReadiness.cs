using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class CompletePhaseTwoInstanceReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RdpPasswordProtected",
                table: "VmInstances",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsInstanceCredentials",
                table: "ImageTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EntryError",
                table: "Containers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EntryReadyAt",
                table: "Containers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "EntryStatus",
                table: "Containers",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.Sql(
                """
                UPDATE "Containers"
                SET "EntryStatus" = 1,
                    "EntryReadyAt" = "StartedAt",
                    "EntryError" = NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "VmInstances"
                SET "Status" = 4,
                    "RdpUrl" = NULL
                WHERE "Status" IN (0, 1, 2);
                """);

            migrationBuilder.DropColumn(
                name: "RdpPassword",
                table: "VmInstances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RdpPasswordProtected",
                table: "VmInstances");

            migrationBuilder.DropColumn(
                name: "SupportsInstanceCredentials",
                table: "ImageTemplates");

            migrationBuilder.DropColumn(
                name: "EntryError",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "EntryReadyAt",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "EntryStatus",
                table: "Containers");

            migrationBuilder.AddColumn<string>(
                name: "RdpPassword",
                table: "VmInstances",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
