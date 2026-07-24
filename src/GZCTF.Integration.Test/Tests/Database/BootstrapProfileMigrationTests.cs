using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class BootstrapProfileMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260714091420_BackfillPhaseNineTeamLabNetworking";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_bootstrap_profiles")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_CreatesBootstrapAndCertificationFactsWithJsonbContracts()
    {
        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await migrator.MigrateAsync();
        }

        await using var migrated = CreateContext();
        var connection = migrated.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN ('BootstrapProfileVersions', 'BootstrapProfileOperationJobs',
                                 'ImageTemplateCapabilityCertifications', 'ImageTemplateCertificationJobs')
              AND column_name IN ('ManifestJson', 'CapabilitiesJson')
              AND data_type = 'jsonb';
            """;
        var jsonbColumns = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(4, jsonbColumns);
        Assert.Empty(await migrated.Database.GetPendingMigrationsAsync());
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true").Options)
    {
        SuppressProjectionRevisionBumps = true
    };
}
