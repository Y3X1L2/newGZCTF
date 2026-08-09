using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class UnifyRuntimeResourceAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CpuUnits",
                table: "FleetCapacityReservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MemoryMiB",
                table: "FleetCapacityReservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "StorageMiB",
                table: "FleetCapacityReservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FairnessKey",
                table: "DeploymentQueueTickets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantKey",
                table: "DeploymentQueueTickets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "DeploymentQueueTickets"
                SET "TenantKey" = CASE
                        WHEN "GameId" IS NOT NULL THEN 'competition:' || "GameId"::text
                        WHEN "OwnerTeamId" IS NOT NULL THEN 'team:' || "OwnerTeamId"::text
                        WHEN "OwnerUserId" IS NOT NULL THEN 'user:' || "OwnerUserId"::text
                        WHEN "TeamLabRuntimeId" IS NOT NULL THEN 'teamlab-runtime:' || "TeamLabRuntimeId"::text
                        ELSE 'ticket:' || "Id"::text
                    END,
                    "FairnessKey" = CASE
                        WHEN "OwnerTeamId" IS NOT NULL THEN 'team:' || "OwnerTeamId"::text
                        WHEN "OwnerUserId" IS NOT NULL THEN 'user:' || "OwnerUserId"::text
                        WHEN "TeamLabRuntimeId" IS NOT NULL THEN 'teamlab-runtime:' || "TeamLabRuntimeId"::text
                        WHEN "GameId" IS NOT NULL THEN 'competition:' || "GameId"::text
                        ELSE 'ticket:' || "Id"::text
                    END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "FairnessKey",
                table: "DeploymentQueueTickets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantKey",
                table: "DeploymentQueueTickets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentQueueTickets_Status_Fairness_Created",
                table: "DeploymentQueueTickets",
                columns: new[] { "Status", "FairnessKey", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeploymentQueueTickets_Status_Fairness_Created",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "CpuUnits",
                table: "FleetCapacityReservations");

            migrationBuilder.DropColumn(
                name: "MemoryMiB",
                table: "FleetCapacityReservations");

            migrationBuilder.DropColumn(
                name: "StorageMiB",
                table: "FleetCapacityReservations");

            migrationBuilder.DropColumn(
                name: "FairnessKey",
                table: "DeploymentQueueTickets");

            migrationBuilder.DropColumn(
                name: "TenantKey",
                table: "DeploymentQueueTickets");
        }
    }
}
