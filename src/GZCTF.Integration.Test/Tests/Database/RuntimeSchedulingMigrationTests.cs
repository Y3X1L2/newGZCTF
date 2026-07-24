using System.Data;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class RuntimeSchedulingMigrationTests : IAsyncLifetime
{
    private const string ExpandMigration = "20260713014106_ExpandPhaseSixRuntimeSchedulingConcurrency";
    private const string BackfillMigration = "20260713014659_BackfillPhaseSixRuntimeSchedulingConcurrency";
    private const string ContractMigration = "20260713015237_ContractPhaseSixRuntimeSchedulingConcurrency";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_six")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task BackfillMigration_ArchivesStaleActiveOrphanTarget()
    {
        var targetId = Guid.Parse("66666666-6666-4666-8666-666666666666");

        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(ExpandMigration);
            await InsertOrphanTargetAsync(context, targetId, "2026-01-01T00:00:00Z");

            await migrator.MigrateAsync(BackfillMigration);
            await migrator.MigrateAsync(ContractMigration);
            await migrator.MigrateAsync();
        }

        await using var migrated = CreateContext();
        Assert.False(await TableExistsAsync(migrated, "DeploymentTargets"));

        await using var command = migrated.Database.GetDbConnection().CreateCommand();
        await migrated.Database.OpenConnectionAsync();
        command.CommandText = """
            SELECT "Status", "Stage", "Operation", "SubjectConcurrencyKey", "CompletedAt", "ErrorMessage"
            FROM "DeploymentQueueTickets"
            WHERE "Id" = @id
            """;
        command.Parameters.Add(new NpgsqlParameter<Guid>("id", targetId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((byte)5, reader.GetByte(0));
        Assert.Equal((byte)18, reader.GetByte(1));
        Assert.Equal((byte)1, reader.GetByte(2));
        Assert.Equal($"legacy-target:{targetId}", reader.GetString(3));
        Assert.False(reader.IsDBNull(4));
        Assert.Contains("stale", reader.GetString(5));
    }

    [Fact]
    public async Task BackfillMigration_RejectsRecentActiveOrphanTarget()
    {
        var targetId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(ExpandMigration);
        await InsertOrphanTargetAsync(context, targetId, null);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync(BackfillMigration));

        Assert.Contains("active orphan DeploymentTargets", exception.MessageText);
        Assert.True(await TableExistsAsync(context, "DeploymentTargets"));
    }

    private static Task InsertOrphanTargetAsync(AppDbContext context, Guid targetId, string? createdAt) =>
        context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "DeploymentTargets"
                ("Id", "TargetNodeId", "Type", "Action", "Payload", "Status", "CreatedAt")
            VALUES
                ({{targetId}}, NULL, 0, 0, '{}', 1,
                 {{(createdAt is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(createdAt))}});
            """);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options) { SuppressProjectionRevisionBumps = true };
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
}
