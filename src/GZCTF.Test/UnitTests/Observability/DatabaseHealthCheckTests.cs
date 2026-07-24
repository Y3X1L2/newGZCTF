using System;
using GZCTF.Services.HealthCheck;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace GZCTF.Test.UnitTests.Observability;

public class DatabaseHealthCheckTests
{
    private static readonly string[] Expected =
    [
        "20260606125458_AddTheoryExamModule",
        "20260721151047_CompletePhaseTwoInstanceReadiness"
    ];

    [Fact]
    public void ExactMigrationSet_IsHealthy()
    {
        var result = DatabaseHealthCheck.EvaluateMigrationState(Expected, Expected, TimeSpan.Zero);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void HistoricalBranchMigration_IsDegradedWithoutBlockingReadiness()
    {
        string[] applied =
        [
            "20260604165857_AddTheoryExamEntities",
            .. Expected
        ];

        var result = DatabaseHealthCheck.EvaluateMigrationState(applied, Expected, TimeSpan.Zero);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(
            ["20260604165857_AddTheoryExamEntities"],
            Assert.IsType<string[]>(result.Data["historicalMigrations"]));
    }

    [Fact]
    public void PendingRequiredMigration_IsUnhealthy()
    {
        var result = DatabaseHealthCheck.EvaluateMigrationState(
            [Expected[0]], Expected, TimeSpan.Zero);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal([Expected[1]], Assert.IsType<string[]>(result.Data["pendingMigrations"]));
    }

    [Fact]
    public void MigrationNewerThanApplication_IsUnhealthy()
    {
        string[] applied =
        [
            .. Expected,
            "20260722120000_FutureSchemaChange"
        ];

        var result = DatabaseHealthCheck.EvaluateMigrationState(applied, Expected, TimeSpan.Zero);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            ["20260722120000_FutureSchemaChange"],
            Assert.IsType<string[]>(result.Data["newerMigrations"]));
    }

    [Fact]
    public void SlowDatabaseCheck_IsDegraded()
    {
        var result = DatabaseHealthCheck.EvaluateMigrationState(
            Expected, Expected, TimeSpan.FromSeconds(2));

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }
}
