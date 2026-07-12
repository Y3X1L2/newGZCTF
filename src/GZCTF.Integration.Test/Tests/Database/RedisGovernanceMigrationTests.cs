using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class RedisGovernanceMigrationTests : IAsyncLifetime
{
    private const string PhaseFourMigration = "20260712080236_BackfillPhaseFourDatabaseGovernance";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_five")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task LatestMigration_BackfillsLeaseOwnersAndProjectionRevisions()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PhaseFourMigration);
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Games"
                ("Id", "AcceptWithoutReview", "BloodBonus", "ContainerCountLimit", "Content",
                 "EndTimeUtc", "GameType", "Hidden", "IsTest", "PracticeMode", "PrivateKey",
                 "PublicKey", "StartTimeUtc", "Summary", "TeamMemberCountLimit", "Title",
                 "WriteupDeadline", "WriteupNote", "WriteupRequired")
            VALUES
                (9001, false, 0, 1, '', '2026-12-31T00:00:00Z', 0, false, false, false,
                 'private', 'public', '2026-01-01T00:00:00Z', '', 5, 'phase-five-game',
                 '2027-01-01T00:00:00Z', '', false);

            INSERT INTO "TheoryPapers"
                ("Id", "CreatedAt", "Description", "GameId", "IsPublished", "Title", "UpdatedAt")
            VALUES
                (9002, CURRENT_TIMESTAMP, '', 9001, true, 'phase-five-paper', CURRENT_TIMESTAMP);

            INSERT INTO "TrainingCourses"
                ("Id", "CreatedAt", "Description", "EnrollmentPolicy", "Slug", "Status", "Summary",
                 "Tags", "Title", "UpdatedAt")
            VALUES
                (9003, CURRENT_TIMESTAMP, '', 'Open', 'phase-five-course', 'Published', '', '[]',
                 'phase-five-course', CURRENT_TIMESTAMP);

            INSERT INTO "Containers"
                ("Id", "ContainerId", "ExpectStopAt", "IP", "Image", "IsProxy", "Port",
                 "PublicPort", "StartedAt", "Status")
            VALUES
                ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'phase-five-container',
                 '2026-12-31T00:00:00Z', '10.0.0.2', 'registry/test:latest', false, 80, 30001,
                 CURRENT_TIMESTAMP, 2);
            """);

        await migrator.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        Assert.Equal(3L, await ScalarAsync<long>(connection,
            "SELECT count(*) FROM \"ProjectionRevisions\""));
        Assert.True(await ScalarAsync<bool>(connection,
            "SELECT \"PublicPortLeaseId\" IS NOT NULL FROM \"Containers\" WHERE \"Id\" = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'"));
        Assert.True(await ScalarAsync<bool>(connection,
            "SELECT to_regclass('\"WorkerNodeMetricSamples\"') IS NOT NULL"));
    }

    [Fact]
    public async Task DeploymentQueueClaim_IsAtomicAcrossConcurrentWorkers()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 3));
        setup.DeploymentQueueTickets.Add(ticket);
        await setup.SaveChangesAsync();

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var claimedAt = DateTimeOffset.UtcNow;
        var claims = await Task.WhenAll(
            QueueManager.TryClaimTicketAsync(firstContext, ticket.Id, claimedAt, CancellationToken.None),
            QueueManager.TryClaimTicketAsync(secondContext, ticket.Id, claimedAt, CancellationToken.None));

        Assert.Single(claims, claimed => claimed);
        await setup.Entry(ticket).ReloadAsync();
        Assert.Equal(DeploymentQueueTicketStatus.Assigned, ticket.Status);
        Assert.NotNull(ticket.AssignedAt);
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
