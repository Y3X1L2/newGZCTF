using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class HardenExternalTeamLabApiContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TeamLabCapture_Idempotency",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKeyHash",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.AddColumn<Guid>(
                name: "ApiOperationId",
                table: "TeamLabTrafficCaptureJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApiOperationId",
                table: "TeamLabTopologyReleases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByOperationId",
                table: "TeamLabTopologies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastMutationOperationId",
                table: "TeamLabTopologies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApiOperationId",
                table: "TeamLabAccessGrants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AppliedAt",
                table: "TeamLabAccessGrants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedDownloadToken",
                table: "TeamLabAccessGrants",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_TeamLabCapture_ApiOperation",
                table: "TeamLabTrafficCaptureJobs",
                column: "ApiOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologyReleases_ApiOperationId",
                table: "TeamLabTopologyReleases",
                column: "ApiOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologies_CreatedByOperationId",
                table: "TeamLabTopologies",
                column: "CreatedByOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabTopologies_LastMutationOperationId",
                table: "TeamLabTopologies",
                column: "LastMutationOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabAccessGrants_ApiOperationId",
                table: "TeamLabAccessGrants",
                column: "ApiOperationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabAccessGrants_ApiOperations_ApiOperationId",
                table: "TeamLabAccessGrants",
                column: "ApiOperationId",
                principalTable: "ApiOperations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTopologies_ApiOperations_CreatedByOperationId",
                table: "TeamLabTopologies",
                column: "CreatedByOperationId",
                principalTable: "ApiOperations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTopologies_ApiOperations_LastMutationOperationId",
                table: "TeamLabTopologies",
                column: "LastMutationOperationId",
                principalTable: "ApiOperations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTopologyReleases_ApiOperations_ApiOperationId",
                table: "TeamLabTopologyReleases",
                column: "ApiOperationId",
                principalTable: "ApiOperations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_ApiOperations_ApiOperationId",
                table: "TeamLabTrafficCaptureJobs",
                column: "ApiOperationId",
                principalTable: "ApiOperations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabAccessGrants_ApiOperations_ApiOperationId",
                table: "TeamLabAccessGrants");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTopologies_ApiOperations_CreatedByOperationId",
                table: "TeamLabTopologies");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTopologies_ApiOperations_LastMutationOperationId",
                table: "TeamLabTopologies");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTopologyReleases_ApiOperations_ApiOperationId",
                table: "TeamLabTopologyReleases");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamLabTrafficCaptureJobs_ApiOperations_ApiOperationId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropIndex(
                name: "UX_TeamLabCapture_ApiOperation",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTopologyReleases_ApiOperationId",
                table: "TeamLabTopologyReleases");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTopologies_CreatedByOperationId",
                table: "TeamLabTopologies");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabTopologies_LastMutationOperationId",
                table: "TeamLabTopologies");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabAccessGrants_ApiOperationId",
                table: "TeamLabAccessGrants");

            migrationBuilder.DropColumn(
                name: "ApiOperationId",
                table: "TeamLabTrafficCaptureJobs");

            migrationBuilder.DropColumn(
                name: "ApiOperationId",
                table: "TeamLabTopologyReleases");

            migrationBuilder.DropColumn(
                name: "CreatedByOperationId",
                table: "TeamLabTopologies");

            migrationBuilder.DropColumn(
                name: "LastMutationOperationId",
                table: "TeamLabTopologies");

            migrationBuilder.DropColumn(
                name: "ApiOperationId",
                table: "TeamLabAccessGrants");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "TeamLabAccessGrants");

            migrationBuilder.DropColumn(
                name: "ProtectedDownloadToken",
                table: "TeamLabAccessGrants");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKeyHash",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "TeamLabTrafficCaptureJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_TeamLabCapture_Idempotency",
                table: "TeamLabTrafficCaptureJobs",
                columns: new[] { "RuntimeId", "Generation", "IdempotencyKeyHash" },
                unique: true);
        }
    }
}
