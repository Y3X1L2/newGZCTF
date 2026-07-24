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

public sealed class InstanceReadinessMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260717040414_AddUserProfileQueryIndexes";
    private const string CurrentMigration = "20260721151047_CompletePhaseTwoInstanceReadiness";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_instance_readiness")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackfillsEntriesAndInvalidatesLegacyVmCredentials()
    {
        var containerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var vmId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var startedAt = DateTimeOffset.Parse("2026-07-20T08:00:00Z");

        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            var game = new Game { Title = "migration-test", PublicKey = "public", PrivateKey = "private" };
            var challenge = new GameChallenge { Title = "vm", Content = "migration", Game = game };
            context.Add(challenge);
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "Containers"
                    ("Id", "Image", "ContainerId", "RuntimeGeneration", "Status", "StartedAt", "ExpectStopAt",
                     "IsProxy", "IP", "Port", "PublicIP", "PublicPort", "PublicPortLeaseId")
                VALUES
                    ({{containerId}}, 'alpine:latest', 'legacy-container', 1, 1, {{startedAt}},
                     {{startedAt.AddHours(2)}}, FALSE, '10.24.0.30', 32768, '203.0.113.10', 30001,
                     {{Guid.Parse("33333333-3333-4333-8333-333333333333")}});

                INSERT INTO "VmInstances"
                    ("Id", "ChallengeId", "UserId", "VmName", "RuntimeGeneration", "ProviderName", "OSType",
                     "Status", "CreatedAt", "RdpUsername", "RdpPassword", "RdpUrl")
                VALUES
                    ({{vmId}}, {{challenge.Id}}, {{Guid.Parse("44444444-4444-4444-8444-444444444444")}},
                     'legacy-vm', 1, 'KVM', 1, 1, {{startedAt}}, 'player', 'legacy-shared-password',
                     'https://gateway.invalid/legacy');

                INSERT INTO "ImageTemplates"
                    ("Name", "OSType", "ImageType", "FileSize", "UploadedAt", "Status", "ContainsMalware")
                VALUES ('legacy-windows', 1, 1, 1024, {{startedAt}}, 0, FALSE);
                """);

            await migrator.MigrateAsync(CurrentMigration);
        }

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using (var containerCommand = connection.CreateCommand())
        {
            containerCommand.CommandText =
                "SELECT \"EntryStatus\", \"EntryReadyAt\", \"EntryError\" FROM \"Containers\" WHERE \"Id\" = @id";
            containerCommand.Parameters.AddWithValue("id", containerId);
            await using var reader = await containerCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((byte)ContainerEntryStatus.Ready, reader.GetByte(0));
            Assert.Equal(startedAt, reader.GetFieldValue<DateTimeOffset>(1));
            Assert.True(reader.IsDBNull(2));
        }

        await using (var vmCommand = connection.CreateCommand())
        {
            vmCommand.CommandText =
                "SELECT \"Status\", \"RdpPasswordProtected\", \"RdpUrl\" FROM \"VmInstances\" WHERE \"Id\" = @id";
            vmCommand.Parameters.AddWithValue("id", vmId);
            await using var reader = await vmCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((byte)VmInstanceStatus.Error, reader.GetByte(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
        }

        await using (var imageCommand = connection.CreateCommand())
        {
            imageCommand.CommandText =
                "SELECT \"SupportsInstanceCredentials\" FROM \"ImageTemplates\" WHERE \"Name\" = 'legacy-windows'";
            Assert.False((bool)(await imageCommand.ExecuteScalarAsync())!);
        }

        Assert.False(await ColumnExistsAsync(connection, "VmInstances", "RdpPassword"));
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options) { SuppressProjectionRevisionBumps = true };
    }

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection connection, string table, string column)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table AND column_name = @column)
            """;
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
