using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalTeamLabRolloutControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabRollouts_AdapterKind_ExternalReference_ReleaseId",
                table: "TeamLabRollouts");

            migrationBuilder.AddColumn<bool>(
                name: "IsDesired",
                table: "TeamLabRolloutTargets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RebuildRequested",
                table: "TeamLabRolloutTargets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByOperationId",
                table: "TeamLabRollouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastMutationOperationId",
                table: "TeamLabRollouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRollouts_ControlScopeId_AdapterKind_ExternalReferenc~",
                table: "TeamLabRollouts",
                columns: new[] { "ControlScopeId", "AdapterKind", "ExternalReference", "ReleaseId" },
                unique: true,
                filter: "\"Status\" NOT IN (5, 8)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabRollouts_ControlScopeId_AdapterKind_ExternalReferenc~",
                table: "TeamLabRollouts");

            migrationBuilder.DropColumn(
                name: "IsDesired",
                table: "TeamLabRolloutTargets");

            migrationBuilder.DropColumn(
                name: "RebuildRequested",
                table: "TeamLabRolloutTargets");

            migrationBuilder.DropColumn(
                name: "CreatedByOperationId",
                table: "TeamLabRollouts");

            migrationBuilder.DropColumn(
                name: "LastMutationOperationId",
                table: "TeamLabRollouts");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabRollouts_AdapterKind_ExternalReference_ReleaseId",
                table: "TeamLabRollouts",
                columns: new[] { "AdapterKind", "ExternalReference", "ReleaseId" },
                unique: true,
                filter: "\"Status\" <> 5");
        }
    }
}
