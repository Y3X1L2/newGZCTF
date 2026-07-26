using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260726083459_IsolateDynamicInstanceFlags")]
    public partial class IsolateDynamicInstanceFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "FlagContexts" AS flag
                SET "ChallengeId" = NULL,
                    "GameChallengeId" = NULL
                WHERE EXISTS (
                    SELECT 1
                    FROM "GameInstances" AS instance
                    JOIN "GameChallenges" AS challenge
                      ON challenge."Id" = instance."ChallengeId"
                    WHERE instance."FlagId" = flag."Id"
                      AND challenge."Type" = 3
                );

                UPDATE "FlagContexts" AS flag
                SET "ExerciseId" = NULL
                WHERE EXISTS (
                    SELECT 1
                    FROM "ExerciseInstances" AS instance
                    JOIN "ExerciseChallenges" AS exercise
                      ON exercise."Id" = instance."ExerciseId"
                    WHERE instance."FlagId" = flag."Id"
                      AND exercise."Type" = 3
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Instance-owned flags intentionally remain detached. Reattaching them on
            // rollback would recreate the data corruption this migration removes.
        }
    }
}
