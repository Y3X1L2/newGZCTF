using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GZCTF.Services.HealthCheck;

public class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var expectedMigrations = dbContext.Database.GetMigrations().ToArray();
            var time = DateTime.UtcNow;
            var appliedMigrations =
                (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();

            return EvaluateMigrationState(appliedMigrations, expectedMigrations, DateTime.UtcNow - time);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }

    internal static HealthCheckResult EvaluateMigrationState(
        IReadOnlyCollection<string> appliedMigrations,
        IReadOnlyCollection<string> expectedMigrations,
        TimeSpan elapsed)
    {
        var expected = expectedMigrations.Distinct(StringComparer.Ordinal).ToArray();
        var applied = appliedMigrations.Distinct(StringComparer.Ordinal).ToArray();
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var appliedSet = applied.ToHashSet(StringComparer.Ordinal);
        var pending = expected.Where(migration => !appliedSet.Contains(migration)).ToArray();
        var unexpected = applied.Where(migration => !expectedSet.Contains(migration)).ToArray();
        var latestExpected = expected.Max(StringComparer.Ordinal);
        var newerThanApplication = latestExpected is null
            ? unexpected
            : unexpected.Where(migration =>
                StringComparer.Ordinal.Compare(migration, latestExpected) > 0).ToArray();

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["expectedMigrationCount"] = expected.Length,
            ["appliedMigrationCount"] = applied.Length,
            ["pendingMigrations"] = pending,
            ["historicalMigrations"] = unexpected.Except(newerThanApplication, StringComparer.Ordinal).ToArray(),
            ["newerMigrations"] = newerThanApplication
        };

        if (pending.Length > 0)
            return HealthCheckResult.Unhealthy("One or more required database migrations are pending.", data: data);

        if (newerThanApplication.Length > 0)
            return HealthCheckResult.Unhealthy(
                "The database contains migrations newer than this application build.", data: data);

        if (elapsed > TimeSpan.FromSeconds(1))
            return HealthCheckResult.Degraded("The database migration check completed slowly.", data: data);

        return unexpected.Length > 0
            ? HealthCheckResult.Degraded(
                "All required migrations are applied; historical migration entries were retained.", data: data)
            : HealthCheckResult.Healthy("All required database migrations are applied.", data);
    }
}
