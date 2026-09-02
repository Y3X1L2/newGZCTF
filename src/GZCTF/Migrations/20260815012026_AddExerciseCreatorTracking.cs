using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseCreatorTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing deployments may contain this nullable ownership column before
            // the migration history was introduced. Keep the upgrade idempotent.
            migrationBuilder.Sql("""
                ALTER TABLE "ExerciseChallenges"
                ADD COLUMN IF NOT EXISTS "CreatedById" uuid NULL;
                CREATE INDEX IF NOT EXISTS "IX_ExerciseChallenges_CreatedById"
                    ON "ExerciseChallenges" ("CreatedById");
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'FK_ExerciseChallenges_AspNetUsers_CreatedById'
                    ) THEN
                        ALTER TABLE "ExerciseChallenges"
                        ADD CONSTRAINT "FK_ExerciseChallenges_AspNetUsers_CreatedById"
                        FOREIGN KEY ("CreatedById") REFERENCES "AspNetUsers" ("Id")
                        ON DELETE SET NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseChallenges_AspNetUsers_CreatedById",
                table: "ExerciseChallenges");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseChallenges_CreatedById",
                table: "ExerciseChallenges");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "ExerciseChallenges");
        }
    }
}
