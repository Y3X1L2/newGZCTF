using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Integration.Test.Tests.Database;

internal static class ExercisePoolMigrationTestCompatibility
{
    public static Task AddCurrentExerciseColumnsAsync(AppDbContext context) =>
        context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ExerciseChallenges"
                ADD COLUMN IF NOT EXISTS "MinimumVisibleRole" smallint NOT NULL DEFAULT 1,
                ADD COLUMN IF NOT EXISTS "PoolSource" smallint NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "SourceChallengeId" integer,
                ADD COLUMN IF NOT EXISTS "SourceGameId" integer,
                ADD COLUMN IF NOT EXISTS "SourceTrainingCourseId" integer,
                ADD COLUMN IF NOT EXISTS "SourceAwdpServiceId" integer;
            """);

    public static Task RemoveCurrentExerciseColumnsAsync(AppDbContext context) =>
        context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ExerciseChallenges"
                DROP COLUMN IF EXISTS "MinimumVisibleRole",
                DROP COLUMN IF EXISTS "PoolSource",
                DROP COLUMN IF EXISTS "SourceChallengeId",
                DROP COLUMN IF EXISTS "SourceGameId",
                DROP COLUMN IF EXISTS "SourceTrainingCourseId",
                DROP COLUMN IF EXISTS "SourceAwdpServiceId";
            """);
}
