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

public sealed class ScopedApiTokenMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260710100000_RemoveLegacyIrScenarioTraining";
    private const string ScopedTokenMigration = "20260710124029_AddScopedApiTokens";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_one_tokens")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_RemovesOwnerlessTokensAndRevokesOwnedLegacyTokens()
    {
        var ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ownedTokenId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var orphanTokenId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            context.Users.Add(new UserInfo
            {
                Id = ownerId,
                UserName = "phase1owner",
                NormalizedUserName = "PHASE1OWNER",
                Email = "phase1-owner@example.test",
                NormalizedEmail = "PHASE1-OWNER@EXAMPLE.TEST",
                EmailConfirmed = true,
                Role = Role.Teacher,
                RegisterTimeUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
            });
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                ALTER TABLE "ApiTokens" ALTER COLUMN "CreatorId" DROP NOT NULL;

                INSERT INTO "ApiTokens"
                    ("Id", "Name", "CreatorId", "CreatedAt", "ExpiresAt", "LastUsedAt", "IsRevoked")
                VALUES
                    ({{ownedTokenId}}, 'owned legacy', {{ownerId}}, now(), NULL, NULL, FALSE),
                    ({{orphanTokenId}}, 'orphan legacy', NULL, now(), NULL, NULL, FALSE);
                """);

            await migrator.MigrateAsync(ScopedTokenMigration);
        }

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Id", "CreatorId", octet_length("SecretHash"), "RevokedAt"
            FROM "ApiTokens"
            ORDER BY "Id"
            """;
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(ownedTokenId, reader.GetGuid(0));
        Assert.Equal(ownerId, reader.GetGuid(1));
        Assert.Equal(32, reader.GetInt32(2));
        Assert.False(reader.IsDBNull(3));
        Assert.False(await reader.ReadAsync());
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options);
    }
}
