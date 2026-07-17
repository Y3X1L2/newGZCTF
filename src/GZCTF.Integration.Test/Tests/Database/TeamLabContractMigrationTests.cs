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
    private const string PreviousMigration = "20260711115423_CompletePhaseOneChallengeApi";
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
            await migrator.MigrateAsync();
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

    [Fact]
    public async Task FoundationMigration_BackfillsShardAndEntryForStoppedLegacyRuntime()
    {
        var workerId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        const int runtimeId = 3201;

        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "AspNetUsers"
                    ("Id", "Role", "LastSignedInUtc", "LastVisitedUtc", "RegisterTimeUtc", "Bio",
                     "RealName", "StdNumber", "ExerciseVisible", "UserName", "NormalizedUserName",
                     "Email", "NormalizedEmail", "EmailConfirmed", "PhoneNumberConfirmed",
                     "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IP")
                VALUES
                    ('55555555-5555-4555-8555-555555555555', 3, '2026-07-11T00:00:00Z',
                     '2026-07-11T00:00:00Z', '2026-07-11T00:00:00Z', '', 'Legacy Owner', '', TRUE,
                     'legacy-owner', 'LEGACY-OWNER', 'legacy-owner@example.test',
                     'LEGACY-OWNER@EXAMPLE.TEST', TRUE, FALSE, FALSE, FALSE, 0, '127.0.0.1'::inet);

                INSERT INTO "Games"
                    ("Id", "Title", "PublicKey", "PrivateKey", "Hidden", "Summary", "Content",
                     "AcceptWithoutReview", "WriteupRequired", "TeamMemberCountLimit", "ContainerCountLimit",
                     "StartTimeUtc", "EndTimeUtc", "WriteupDeadline", "WriteupNote", "BloodBonus",
                     "PracticeMode", "IsTest", "GameType")
                VALUES
                    (3202, 'Legacy TeamLab', 'public-key', 'private-key', FALSE, '', '', TRUE, FALSE,
                     4, 4, '2026-07-11T00:00:00Z', '2026-07-12T00:00:00Z',
                     '2026-07-13T00:00:00Z', '', 0, FALSE, TRUE, 3);

                INSERT INTO "Teams" ("Id", "Name", "Locked", "InviteToken", "CaptainId")
                VALUES
                    (3203, 'Legacy Team', FALSE, 'legacy-team-invite',
                     '55555555-5555-4555-8555-555555555555');

                INSERT INTO "WorkerNodes"
                    ("Id", "Name", "HostAddress", "AuthToken", "Capabilities", "Status", "CpuLoad",
                     "MemoryLoad", "CurrentContainers", "MaxContainers", "CurrentVms", "MaxVms",
                     "UsedPorts", "TotalPorts", "RegisteredAt")
                VALUES
                    ({{workerId}}, 'legacy-worker', '10.24.0.30', 'legacy-worker-token', 1, 1, 0, 0,
                     0, 20, 0, 5, 0, 100, '2026-07-11T00:00:00Z');

                INSERT INTO "PenetrationConfigs"
                    ("Id", "GameId", "BaseCidr", "TeamSubnetPrefix", "NetworkSubnetPrefix",
                     "MaxResetCount", "PublishedVersion", "Status", "UpdatedAt", "PublishedAt")
                VALUES
                    (3204, 3202, '10.180.0.0/16', 24, 28, 3, 1, 'Published',
                     '2026-07-11T00:00:00Z', '2026-07-11T00:00:00Z');

                INSERT INTO "PenetrationNetworks"
                    ("Id", "ConfigId", "Name", "Slug", "Cidr", "OrderIndex", "IsEntry", "TopologyKey")
                VALUES
                    (3205, 3204, 'Entry network', 'entry-network', '10.180.1.0/28', 0, FALSE, 'entry-network');

                INSERT INTO "PenetrationNodes"
                    ("Id", "ConfigId", "NetworkId", "Name", "NodeType", "CpuCount", "MemoryLimit",
                     "StorageLimit", "ExposePort", "IsEntry", "PublishPort", "EnvironmentVariables",
                     "PositionX", "PositionY", "OrderIndex", "TopologyKey", "AllowRouting")
                VALUES
                    (3206, 3204, 3205, 'Legacy web', 'Docker', 1, 256, 1024, 80, TRUE, FALSE,
                     '{}', 0, 0, 0, 'legacy-web', FALSE);

                INSERT INTO "TeamLabRuntimes"
                    ("Id", "GameId", "TeamId", "PublishedVersion", "WorkerNodeId", "NetworkPrefix",
                     "Status", "IsOpenToPlayers", "LastError", "CreatedAt", "UpdatedAt")
                VALUES
                    ({{runtimeId}}, 3202, 3203, 1, {{workerId}}, '10.180.1.0/28', 10, FALSE,
                     'legacy runtime stopped', '2026-07-11T00:00:00Z', '2026-07-11T01:00:00Z');

                INSERT INTO "TeamLabRuntimeNetworks"
                    ("Id", "RuntimeId", "TopologyKey", "Name", "Cidr", "GatewayIp", "BridgeName",
                     "ShardId", "WorkerNodeId")
                VALUES
                    (3207, {{runtimeId}}, 'entry-network', 'Entry network', '10.180.1.0/28',
                     '10.180.1.1', 'gz-entry', NULL, {{workerId}});

                INSERT INTO "TeamLabRuntimeAssets"
                    ("Id", "RuntimeId", "Kind", "TopologyKey", "Name", "RuntimeResourceId", "Status",
                     "LastError", "NetworkKey", "InterfaceSummaryJson", "ShardId", "WorkerNodeId")
                VALUES
                    (3208, {{runtimeId}}, 0, 'legacy-web', 'Legacy web', 'legacy-container', 10,
                     NULL, 'entry-network', '[]', NULL, NULL);
                """);

            await migrator.MigrateAsync(FoundationMigration);
            await migrator.MigrateAsync(ContractMigration);
            await migrator.MigrateAsync();
        }

        await using var migrated = CreateContext();
        Assert.Equal(1, await ScalarAsync<int>(migrated,
            "SELECT count(*)::int FROM \"TeamLabRuntimes\" WHERE \"Id\" = 3201"));
        Assert.Equal(1, await ScalarAsync<int>(migrated,
            "SELECT count(*)::int FROM \"TeamLabRuntimeShards\" WHERE \"RuntimeId\" = 3201 AND \"Generation\" = 1"));
        Assert.Equal(1, await ScalarAsync<int>(migrated,
            "SELECT count(*)::int FROM \"TeamLabRuntimeNetworks\" WHERE \"RuntimeId\" = 3201 AND \"ShardId\" IS NOT NULL"));
        Assert.Equal(1, await ScalarAsync<int>(migrated,
            "SELECT count(*)::int FROM \"TeamLabRuntimeAssets\" WHERE \"RuntimeId\" = 3201 AND \"ShardId\" IS NOT NULL"));
        Assert.Equal(1, await ScalarAsync<int>(migrated,
            "SELECT count(*)::int FROM \"TeamLabRuntimes\" runtime JOIN \"TeamLabRuntimeShards\" shard ON shard.\"Id\" = runtime.\"EntryShardId\" AND shard.\"RuntimeId\" = runtime.\"Id\" AND shard.\"Generation\" = runtime.\"Generation\" WHERE runtime.\"Id\" = 3201"));
        Assert.False(await ColumnExistsAsync(migrated, "TeamLabRuntimes", "GameId"));
        Assert.False(await ColumnExistsAsync(migrated, "TeamLabRuntimes", "TeamId"));
        Assert.False(await ColumnExistsAsync(migrated, "TeamLabRuntimes", "WorkerNodeId"));
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

    private static async Task<T> ScalarAsync<T>(AppDbContext context, string sql)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
