using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class IntegrateNetlabSceneBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RuntimeId",
                table: "PenetrationTeamRuntimeBindings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "BlueprintVersion",
                table: "PenetrationTeamRuntimeBindings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BlueprintVersionId",
                table: "PenetrationTeamRuntimeBindings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreationRequestId",
                table: "PenetrationTeamRuntimeBindings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentId",
                table: "PenetrationTeamRuntimeBindings",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RuntimeId",
                table: "PenetrationResetRecords",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentId",
                table: "PenetrationResetRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteOperationId",
                table: "PenetrationResetRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TopologyId",
                table: "PenetrationGameLabBindings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "AccessOpen",
                table: "PenetrationGameLabBindings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BlueprintId",
                table: "PenetrationGameLabBindings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlueprintVersionId",
                table: "PenetrationGameLabBindings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationTeamRuntimeBindings_EnvironmentId",
                table: "PenetrationTeamRuntimeBindings",
                column: "EnvironmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationResetRecords_EnvironmentId_TargetGeneration",
                table: "PenetrationResetRecords",
                columns: new[] { "EnvironmentId", "TargetGeneration" },
                unique: true,
                filter: "\"EnvironmentId\" IS NOT NULL AND \"Status\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PenetrationTeamRuntimeBindings_EnvironmentId",
                table: "PenetrationTeamRuntimeBindings");

            migrationBuilder.DropIndex(
                name: "IX_PenetrationResetRecords_EnvironmentId_TargetGeneration",
                table: "PenetrationResetRecords");

            migrationBuilder.DropColumn(
                name: "BlueprintVersion",
                table: "PenetrationTeamRuntimeBindings");

            migrationBuilder.DropColumn(
                name: "BlueprintVersionId",
                table: "PenetrationTeamRuntimeBindings");

            migrationBuilder.DropColumn(
                name: "CreationRequestId",
                table: "PenetrationTeamRuntimeBindings");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "PenetrationTeamRuntimeBindings");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "PenetrationResetRecords");

            migrationBuilder.DropColumn(
                name: "RemoteOperationId",
                table: "PenetrationResetRecords");

            migrationBuilder.DropColumn(
                name: "AccessOpen",
                table: "PenetrationGameLabBindings");

            migrationBuilder.DropColumn(
                name: "BlueprintId",
                table: "PenetrationGameLabBindings");

            migrationBuilder.DropColumn(
                name: "BlueprintVersionId",
                table: "PenetrationGameLabBindings");

            migrationBuilder.AlterColumn<int>(
                name: "RuntimeId",
                table: "PenetrationTeamRuntimeBindings",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RuntimeId",
                table: "PenetrationResetRecords",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TopologyId",
                table: "PenetrationGameLabBindings",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
