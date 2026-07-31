using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

public partial class HardenPenetrationTeamLabLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CompletedAt",
            table: "PenetrationResetRecords",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<byte>(
            name: "FailureClass",
            table: "PenetrationResetRecords",
            type: "smallint",
            nullable: false,
            defaultValue: (byte)0);
        migrationBuilder.AddColumn<Guid>(
            name: "OperationId",
            table: "PenetrationResetRecords",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<byte>(
            name: "Status",
            table: "PenetrationResetRecords",
            type: "smallint",
            nullable: false,
            defaultValue: (byte)2);
        migrationBuilder.AddColumn<int>(
            name: "TargetGeneration",
            table: "PenetrationResetRecords",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<Guid>(
            name: "DestroyOperationId",
            table: "PenetrationTeamRuntimeBindings",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DestroyedAt",
            table: "PenetrationTeamRuntimeBindings",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<byte>(
            name: "Status",
            table: "PenetrationTeamRuntimeBindings",
            type: "smallint",
            nullable: false,
            defaultValue: (byte)0);

        migrationBuilder.Sql("""
            UPDATE "PenetrationResetRecords" reset
            SET "OperationId" = gen_random_uuid(),
                "CompletedAt" = reset."ResetAt",
                "TargetGeneration" = GREATEST(runtime."Generation", 1)
            FROM "TeamLabRuntimes" runtime
            WHERE runtime."Id" = reset."RuntimeId";

            UPDATE "PenetrationResetRecords"
            SET "OperationId" = gen_random_uuid(),
                "CompletedAt" = "ResetAt",
                "TargetGeneration" = 1
            WHERE "OperationId" IS NULL;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "OperationId",
            table: "PenetrationResetRecords",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
        migrationBuilder.AlterColumn<byte>(
            name: "Status",
            table: "PenetrationResetRecords",
            type: "smallint",
            nullable: false,
            defaultValue: (byte)0,
            oldClrType: typeof(byte),
            oldType: "smallint",
            oldDefaultValue: (byte)2);

        migrationBuilder.CreateIndex(
            name: "IX_PenetrationResetRecords_OperationId",
            table: "PenetrationResetRecords",
            column: "OperationId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_PenetrationResetRecords_RuntimeId_TargetGeneration",
            table: "PenetrationResetRecords",
            columns: new[] { "RuntimeId", "TargetGeneration" },
            unique: true,
            filter: "\"Status\" IN (0, 1)");
        migrationBuilder.CreateIndex(
            name: "IX_PenetrationTeamRuntimeBindings_DestroyOperationId",
            table: "PenetrationTeamRuntimeBindings",
            column: "DestroyOperationId",
            unique: true,
            filter: "\"DestroyOperationId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PenetrationResetRecords_OperationId",
            table: "PenetrationResetRecords");
        migrationBuilder.DropIndex(
            name: "IX_PenetrationResetRecords_RuntimeId_TargetGeneration",
            table: "PenetrationResetRecords");
        migrationBuilder.DropIndex(
            name: "IX_PenetrationTeamRuntimeBindings_DestroyOperationId",
            table: "PenetrationTeamRuntimeBindings");

        migrationBuilder.DropColumn(name: "CompletedAt", table: "PenetrationResetRecords");
        migrationBuilder.DropColumn(name: "FailureClass", table: "PenetrationResetRecords");
        migrationBuilder.DropColumn(name: "OperationId", table: "PenetrationResetRecords");
        migrationBuilder.DropColumn(name: "Status", table: "PenetrationResetRecords");
        migrationBuilder.DropColumn(name: "TargetGeneration", table: "PenetrationResetRecords");
        migrationBuilder.DropColumn(name: "DestroyOperationId", table: "PenetrationTeamRuntimeBindings");
        migrationBuilder.DropColumn(name: "DestroyedAt", table: "PenetrationTeamRuntimeBindings");
        migrationBuilder.DropColumn(name: "Status", table: "PenetrationTeamRuntimeBindings");
    }
}
