using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class UnifiedRuntimeResourceMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260720153831_SimplifyVmImageLifecycle";
    private const string TargetMigration = "20260722090000_UnifyRuntimeResourceAccounting";

    private static readonly Guid CompetitionTicketId =
        Guid.Parse("20000000-0000-7000-8000-000000000001");
    private static readonly Guid TeamTicketId =
        Guid.Parse("20000000-0000-7000-8000-000000000002");
    private static readonly Guid UserTicketId =
        Guid.Parse("20000000-0000-7000-8000-000000000003");
    private static readonly Guid RuntimeTicketId =
        Guid.Parse("20000000-0000-7000-8000-000000000004");
    private static readonly Guid FallbackTicketId =
        Guid.Parse("20000000-0000-7000-8000-000000000005");
    private static readonly Guid OwnerUserId =
        Guid.Parse("30000000-0000-4000-8000-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_unified_runtime_resources")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackfillsQueueIdentityAndAddsResourceSchema()
    {
        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "DeploymentQueueTickets"
                    ("Id", "Kind", "Operation", "Status", "Stage", "OwnerTeamId", "OwnerUserId",
                     "GameId", "TeamLabRuntimeId", "DockerSlots", "VmSlots", "Generation",
                     "ActiveIdentity", "SubjectConcurrencyKey", "Retryable", "AttemptCount", "CreatedAt")
                VALUES
                    ({{CompetitionTicketId}}, 7, 1, 4, 17, 41, {{OwnerUserId}}, 31, 51, 0, 1, 1,
                     'active:competition', 'subject:competition', false, 0, CURRENT_TIMESTAMP),
                    ({{TeamTicketId}}, 1, 1, 4, 17, 42, NULL, NULL, NULL, 1, 0, 1,
                     'active:team', 'subject:team', false, 0, CURRENT_TIMESTAMP),
                    ({{UserTicketId}}, 2, 1, 4, 17, NULL, {{OwnerUserId}}, NULL, NULL, 1, 0, 1,
                     'active:user', 'subject:user', false, 0, CURRENT_TIMESTAMP),
                    ({{RuntimeTicketId}}, 7, 1, 4, 17, NULL, NULL, NULL, 52, 0, 1, 1,
                     'active:runtime', 'subject:runtime', false, 0, CURRENT_TIMESTAMP),
                    ({{FallbackTicketId}}, 5, 1, 4, 17, NULL, NULL, NULL, NULL, 1, 0, 1,
                     'active:fallback', 'subject:fallback', false, 0, CURRENT_TIMESTAMP);
                """);
            await migrator.MigrateAsync(TargetMigration);
        }

        await using var migrated = CreateContext();
        var tickets = await migrated.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.Id == CompetitionTicketId ||
                           item.Id == TeamTicketId ||
                           item.Id == UserTicketId ||
                           item.Id == RuntimeTicketId ||
                           item.Id == FallbackTicketId)
            .ToDictionaryAsync(item => item.Id);

        Assert.Equal("competition:31", tickets[CompetitionTicketId].TenantKey);
        Assert.Equal("team:41", tickets[CompetitionTicketId].FairnessKey);
        Assert.Equal("team:42", tickets[TeamTicketId].TenantKey);
        Assert.Equal("team:42", tickets[TeamTicketId].FairnessKey);
        Assert.Equal($"user:{OwnerUserId}", tickets[UserTicketId].TenantKey);
        Assert.Equal($"user:{OwnerUserId}", tickets[UserTicketId].FairnessKey);
        Assert.Equal("teamlab-runtime:52", tickets[RuntimeTicketId].TenantKey);
        Assert.Equal("teamlab-runtime:52", tickets[RuntimeTicketId].FairnessKey);
        Assert.Equal($"ticket:{FallbackTicketId}", tickets[FallbackTicketId].TenantKey);
        Assert.Equal($"ticket:{FallbackTicketId}", tickets[FallbackTicketId].FairnessKey);

        Assert.Equal(3, await migrated.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'FleetCapacityReservations'
              AND column_name IN ('CpuUnits', 'MemoryMiB', 'StorageMiB')
              AND data_type = 'bigint'
              AND is_nullable = 'NO'
            """).SingleAsync());
        Assert.Equal(2, await migrated.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'DeploymentQueueTickets'
              AND column_name IN ('TenantKey', 'FairnessKey')
              AND is_nullable = 'NO'
            """).SingleAsync());
        Assert.Equal(1, await migrated.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM pg_indexes
            WHERE tablename = 'DeploymentQueueTickets'
              AND indexname = 'IX_DeploymentQueueTickets_Status_Fairness_Created'
            """).SingleAsync());
        Assert.Equal(1, await migrated.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM pg_indexes
            WHERE tablename = 'DeploymentQueueTickets'
              AND indexname = 'UX_DeploymentQueueTickets_SubjectConcurrencyKey'
            """).SingleAsync());
        await migrated.Database.MigrateAsync();
        Assert.Empty(await migrated.Database.GetPendingMigrationsAsync());
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true").Options)
    {
        SuppressProjectionRevisionBumps = true
    };
}
