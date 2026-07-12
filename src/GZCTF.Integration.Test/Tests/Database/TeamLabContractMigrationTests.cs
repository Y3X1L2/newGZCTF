using System.Data;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabContractMigrationTests : IAsyncLifetime
{
    private const string FoundationMigration = "20260711144502_AddIndependentTeamLabFoundation";
    private const string ContractMigration = "20260711170329_RemovePenetrationTopologyRuntimeCompatibility";
    private const string ReliabilityMigration = "20260712054103_CompleteTeamLabRuntimeReliability";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_three")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ContractMigration_RemovesLegacyRuntimeSchemaWithoutModelDrift()
    {
        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(FoundationMigration);
            await migrator.MigrateAsync(ContractMigration);
            await migrator.MigrateAsync(ReliabilityMigration);
        }

        await using var migrated = CreateContext();
        foreach (var table in RemovedTables)
            Assert.False(await TableExistsAsync(migrated, table), $"Legacy table {table} still exists.");

        Assert.False(await ColumnExistsAsync(migrated, "TeamLabRuntimes", "GameId"));
        Assert.False(await ColumnExistsAsync(migrated, "TeamLabRuntimes", "TeamId"));
        Assert.False(await ColumnExistsAsync(migrated, "TeamLabRuntimes", "WorkerNodeId"));
        Assert.True(await ColumnExistsAsync(migrated, "TeamLabTopologies", "EditorMetadataJson"));
        Assert.Empty(await migrated.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task ContractMigration_RejectsActiveLegacyEnvironmentWithoutRuntimeBinding()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(FoundationMigration);

        var user = new UserInfo
        {
            Id = Guid.Parse("33333333-3333-4333-8333-333333333333"),
            UserName = "phase3-owner",
            NormalizedUserName = "PHASE3-OWNER",
            Email = "phase3-owner@example.test",
            NormalizedEmail = "PHASE3-OWNER@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Admin,
            RegisterTimeUtc = DateTimeOffset.Parse("2026-07-11T00:00:00Z")
        };
        var game = new Game { Id = 3101, Title = "Legacy TeamLab" };
        var team = new Team { Id = 3102, Name = "Legacy Team", CaptainId = user.Id };
        context.Users.Add(user);
        context.Games.Add(game);
        context.Teams.Add(team);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "PenetrationTeamEnvironments"
                ("Id", "GameId", "TeamId", "NodeId", "TeamIndex", "NetworkPrefix",
                 "PublishedVersion", "Status", "ResetCount", "CleanupRetryCount", "CreatedAt")
            VALUES
                (3199, {{game.Id}}, {{team.Id}}, NULL, 0, '10.180.0.0/24',
                 1, 'Running', 0, 0, {{DateTimeOffset.Parse("2026-07-11T00:00:00Z")}});
            """);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync(ContractMigration));

        Assert.Contains("active environment has no TeamLab runtime binding", exception.MessageText);
        Assert.True(await TableExistsAsync(context, "PenetrationTeamEnvironments"));
    }

    private static readonly string[] RemovedTables =
    [
        "PenetrationConfigs",
        "PenetrationPublishedSnapshots",
        "PenetrationNetworks",
        "PenetrationNodes",
        "PenetrationInterfaces",
        "PenetrationEdges",
        "PenetrationScoreItems",
        "PenetrationTeamEnvironments",
        "PenetrationDeploymentEvents",
        "PenetrationRuntimeNodes",
        "PenetrationRuntimeRoutes"
    ];

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<bool> TableExistsAsync(AppDbContext context, string tableName)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@qualified_name) IS NOT NULL";
        command.Parameters.AddWithValue("qualified_name", $"public.\"{tableName}\"");
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext context, string tableName, string columnName)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table AND column_name = @column)
            """;
        command.Parameters.AddWithValue("table", tableName);
        command.Parameters.AddWithValue("column", columnName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
