using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabWebhookSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLabWebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EventTypesJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    SigningSecretEncrypted = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryCursor = table.Column<long>(type: "bigint", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    NextDeliveryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabWebhookSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabWebhookSubscriptions_TeamLabControlScopes_ControlSco~",
                        column: x => x.ControlScopeId,
                        principalTable: "TeamLabControlScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamLabWebhookDeliveryFailures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<long>(type: "bigint", nullable: false),
                    EventStage = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLabWebhookDeliveryFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLabWebhookDeliveryFailures_TeamLabWebhookSubscriptions_~",
                        column: x => x.SubscriptionId,
                        principalTable: "TeamLabWebhookSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabWebhookDeliveryFailures_SubscriptionId_EventId",
                table: "TeamLabWebhookDeliveryFailures",
                columns: new[] { "SubscriptionId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabWebhookSubscriptions_Active_NextDeliveryAt",
                table: "TeamLabWebhookSubscriptions",
                columns: new[] { "Active", "NextDeliveryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabWebhookSubscriptions_ApiOperationId",
                table: "TeamLabWebhookSubscriptions",
                column: "ApiOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabWebhookSubscriptions_ControlScopeId_Active",
                table: "TeamLabWebhookSubscriptions",
                columns: new[] { "ControlScopeId", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLabWebhookSubscriptions_PublicId",
                table: "TeamLabWebhookSubscriptions",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLabWebhookSubscriptions_PublicId",
                table: "TeamLabWebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabWebhookSubscriptions_ControlScopeId_Active",
                table: "TeamLabWebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabWebhookSubscriptions_ApiOperationId",
                table: "TeamLabWebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabWebhookSubscriptions_Active_NextDeliveryAt",
                table: "TeamLabWebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_TeamLabWebhookDeliveryFailures_SubscriptionId_EventId",
                table: "TeamLabWebhookDeliveryFailures");

            migrationBuilder.DropTable(
                name: "TeamLabWebhookDeliveryFailures");

            migrationBuilder.DropTable(
                name: "TeamLabWebhookSubscriptions");
        }
    }
}
