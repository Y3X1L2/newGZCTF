using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AlignTeamLabExecutionRuntimeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Databases upgraded through either TeamLab migration lineage converge here without
            // rollback. The V2 snapshot table itself was created by the preceding migration.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'TeamLabRuntimes'
                          AND column_name = 'IsScenarioBuild') THEN
                        ALTER TABLE "TeamLabRuntimes"
                            ADD COLUMN "IsScenarioBuild" boolean NOT NULL DEFAULT FALSE;
                    ELSE
                        ALTER TABLE "TeamLabRuntimes"
                            ALTER COLUMN "IsScenarioBuild" SET DEFAULT FALSE;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'TeamLabRuntimeAssets'
                          AND column_name = 'Stateless') THEN
                        ALTER TABLE "TeamLabRuntimeAssets"
                            ALTER COLUMN "Stateless" SET DEFAULT FALSE;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'TeamLabExecutionPlanSnapshots'
                          AND column_name = 'PlanDigest') THEN
                        ALTER TABLE "TeamLabExecutionPlanSnapshots"
                            ALTER COLUMN "PlanDigest" TYPE character varying(96);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PlanDigest",
                table: "TeamLabExecutionPlanSnapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(96)",
                oldMaxLength: 96);
        }
    }
}
