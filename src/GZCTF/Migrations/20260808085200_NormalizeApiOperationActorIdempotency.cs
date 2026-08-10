using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeApiOperationActorIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiOperations_ApiTokenId_RouteKey_IdempotencyKey",
                table: "ApiOperations");

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperations_ActorUserId_RouteKey_IdempotencyKey",
                table: "ApiOperations",
                columns: new[] { "ActorUserId", "RouteKey", "IdempotencyKey" },
                unique: true,
                filter: "\"ApiTokenId\" IS NULL AND \"ActorUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperations_ApiTokenId_RouteKey_IdempotencyKey",
                table: "ApiOperations",
                columns: new[] { "ApiTokenId", "RouteKey", "IdempotencyKey" },
                unique: true,
                filter: "\"ApiTokenId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiOperations_ActorUserId_RouteKey_IdempotencyKey",
                table: "ApiOperations");

            migrationBuilder.DropIndex(
                name: "IX_ApiOperations_ApiTokenId_RouteKey_IdempotencyKey",
                table: "ApiOperations");

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperations_ApiTokenId_RouteKey_IdempotencyKey",
                table: "ApiOperations",
                columns: new[] { "ApiTokenId", "RouteKey", "IdempotencyKey" },
                unique: true);
        }
    }
}
