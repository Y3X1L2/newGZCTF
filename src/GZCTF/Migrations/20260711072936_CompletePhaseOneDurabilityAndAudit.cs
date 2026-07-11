using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class CompletePhaseOneDurabilityAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalApiRequestAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Scopes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RouteKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestBytes = table.Column<long>(type: "bigint", nullable: false),
                    ResponseBytes = table.Column<long>(type: "bigint", nullable: false),
                    RemoteIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IdempotencyReused = table.Column<bool>(type: "boolean", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiRequestAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalApiRequestAudits_ApiOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ApiOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ExternalApiRequestAudits_ApiTokens_ApiTokenId",
                        column: x => x.ApiTokenId,
                        principalTable: "ApiTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ExternalApiRequestAudits_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperations_DeploymentQueueTicketId",
                table: "ApiOperations",
                column: "DeploymentQueueTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiRequestAudits_ActorUserId",
                table: "ExternalApiRequestAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiRequestAudits_ApiTokenId",
                table: "ExternalApiRequestAudits",
                column: "ApiTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiRequestAudits_CreatedAt",
                table: "ExternalApiRequestAudits",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiRequestAudits_OperationId",
                table: "ExternalApiRequestAudits",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiRequestAudits_TraceId",
                table: "ExternalApiRequestAudits",
                column: "TraceId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiOperations_ApiTokens_ApiTokenId",
                table: "ApiOperations",
                column: "ApiTokenId",
                principalTable: "ApiTokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiOperations_AspNetUsers_ActorUserId",
                table: "ApiOperations",
                column: "ActorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiOperations_DeploymentQueueTickets_DeploymentQueueTicketId",
                table: "ApiOperations",
                column: "DeploymentQueueTicketId",
                principalTable: "DeploymentQueueTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiOperations_ApiTokens_ApiTokenId",
                table: "ApiOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiOperations_AspNetUsers_ActorUserId",
                table: "ApiOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiOperations_DeploymentQueueTickets_DeploymentQueueTicketId",
                table: "ApiOperations");

            migrationBuilder.DropTable(
                name: "ExternalApiRequestAudits");

            migrationBuilder.DropIndex(
                name: "IX_ApiOperations_DeploymentQueueTicketId",
                table: "ApiOperations");
        }
    }
}
