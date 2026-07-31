using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260727090000_HardenTeamLabReleaseLifecycleBoundaries")]
public partial class HardenTeamLabReleaseLifecycleBoundaries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BakeAttemptOperationId",
            table: "TeamLabReleaseAssetArtifacts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NetworkKey",
            table: "TeamLabNetworkLeases",
            type: "character varying(63)",
            maxLength: 63,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TopologyReleaseId",
            table: "TeamLabNetworkLeases",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "TeamLabNetworkLeases" AS lease
            SET "TopologyReleaseId" = runtime."TopologyReleaseId",
                "NetworkKey" = network."Key"
            FROM "TeamLabRuntimes" AS runtime,
                 "TeamLabTopologyNetworks" AS network
            WHERE runtime."Id" = lease."RuntimeId"
              AND network."Id" = lease."TopologyNetworkId";

            UPDATE "TeamLabReleaseAssetArtifacts" AS artifact
            SET "BakeAttemptOperationId" = release."ApiOperationId"
            FROM "TeamLabTopologyReleases" AS release
            WHERE release."Id" = artifact."ReleaseId"
              AND artifact."BakeAttemptOperationId" IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "NetworkKey",
            table: "TeamLabNetworkLeases",
            type: "character varying(63)",
            maxLength: 63,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(63)",
            oldMaxLength: 63,
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "TopologyReleaseId",
            table: "TeamLabNetworkLeases",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.DropForeignKey(
            name: "FK_TeamLabNetworkLeases_TeamLabTopologyNetworks_TopologyNetwor~",
            table: "TeamLabNetworkLeases");

        migrationBuilder.DropIndex(
            name: "IX_TeamLabNetworkLeases_RuntimeId_Generation_TopologyNetworkId",
            table: "TeamLabNetworkLeases");

        migrationBuilder.DropIndex(
            name: "IX_TeamLabNetworkLeases_TopologyNetworkId",
            table: "TeamLabNetworkLeases");

        migrationBuilder.DropColumn(
            name: "TopologyNetworkId",
            table: "TeamLabNetworkLeases");

        migrationBuilder.CreateIndex(
            name: "IX_TeamLabNetworkLeases_RuntimeId_Generation_NetworkKey",
            table: "TeamLabNetworkLeases",
            columns: new[] { "RuntimeId", "Generation", "NetworkKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TeamLabNetworkLeases_TopologyReleaseId_NetworkKey",
            table: "TeamLabNetworkLeases",
            columns: new[] { "TopologyReleaseId", "NetworkKey" });

        migrationBuilder.AddForeignKey(
            name: "FK_TeamLabNetworkLeases_TeamLabTopologyReleases_TopologyReleas~",
            table: "TeamLabNetworkLeases",
            column: "TopologyReleaseId",
            principalTable: "TeamLabTopologyReleases",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_TeamLabNetworkLeases_TeamLabTopologyReleases_TopologyReleas~",
            table: "TeamLabNetworkLeases");

        migrationBuilder.DropIndex(
            name: "IX_TeamLabNetworkLeases_RuntimeId_Generation_NetworkKey",
            table: "TeamLabNetworkLeases");

        migrationBuilder.DropIndex(
            name: "IX_TeamLabNetworkLeases_TopologyReleaseId_NetworkKey",
            table: "TeamLabNetworkLeases");

        migrationBuilder.AddColumn<int>(
            name: "TopologyNetworkId",
            table: "TeamLabNetworkLeases",
            type: "integer",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "TeamLabNetworkLeases" AS lease
            SET "TopologyNetworkId" = network."Id"
            FROM "TeamLabTopologyReleases" AS release,
                 "TeamLabTopologyNetworks" AS network
            WHERE release."Id" = lease."TopologyReleaseId"
              AND network."TopologyId" = release."TopologyId"
              AND network."Key" = lease."NetworkKey";

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM "TeamLabNetworkLeases"
                    WHERE "TopologyNetworkId" IS NULL
                ) THEN
                    RAISE EXCEPTION 'Cannot restore mutable topology network references after release network divergence.';
                END IF;
            END $$;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "TopologyNetworkId",
            table: "TeamLabNetworkLeases",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TeamLabNetworkLeases_RuntimeId_Generation_TopologyNetworkId",
            table: "TeamLabNetworkLeases",
            columns: new[] { "RuntimeId", "Generation", "TopologyNetworkId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TeamLabNetworkLeases_TopologyNetworkId",
            table: "TeamLabNetworkLeases",
            column: "TopologyNetworkId");

        migrationBuilder.AddForeignKey(
            name: "FK_TeamLabNetworkLeases_TeamLabTopologyNetworks_TopologyNetwor~",
            table: "TeamLabNetworkLeases",
            column: "TopologyNetworkId",
            principalTable: "TeamLabTopologyNetworks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropColumn(
            name: "NetworkKey",
            table: "TeamLabNetworkLeases");

        migrationBuilder.DropColumn(
            name: "TopologyReleaseId",
            table: "TeamLabNetworkLeases");

        migrationBuilder.DropColumn(
            name: "BakeAttemptOperationId",
            table: "TeamLabReleaseAssetArtifacts");
    }
}
