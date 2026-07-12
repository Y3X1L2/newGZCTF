using System.Data;
using GZCTF.Infrastructure.Persistence.Governance;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class DatabaseGovernanceMigrationTests : IAsyncLifetime
{
    private const string PhaseThreeMigration = "20260712054103_CompleteTeamLabRuntimeReliability";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_four")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task LatestMigration_PreservesFactsAndEnforcesGovernanceContracts()
    {
        var seed = await SeedPhaseThreeFactsAsync();

        await using (var context = CreateContext())
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Asia/Shanghai'");
            var migrator = context.Database.GetService<IMigrator>();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Contains("duplicate Participation", rejected.MessageText);
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"Participations\" WHERE \"Id\" = 930002");
            var lateQuestion = new TheoryQuestionBankItem
            {
                Type = TheoryQuestionType.SingleChoice,
                BankName = " Contract Window ",
                Title = "Late migration question",
                Options = ["A", "B"],
                AnswerIndexes = [1]
            };
            context.TheoryQuestionBankItems.Add(lateQuestion);
            await context.SaveChangesAsync();
            await migrator.MigrateAsync();
            seed = seed with { LateQuestionId = lateQuestion.Id };
        }

        await AssertMigrationContractAsync(seed);
        await AssertOperationalGovernanceAsync(seed);
    }

    private async Task<SeedFacts> SeedPhaseThreeFactsAsync()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PhaseThreeMigration);

        var node = new WorkerNode
        {
            Name = "phase4-node",
            HostAddress = "10.24.0.200",
            AuthToken = "phase4-test-token",
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm
        };
        var template = new ImageTemplate
        {
            Name = "phase4-template",
            ImageType = ImageType.Docker,
            OSType = OSType.Linux,
            Status = ImageStatus.Ready,
            ImageHash = new string('a', 64)
        };
        var topology = new TeamLabTopology { Name = "phase4-topology" };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology,
            Version = 1,
            SourceRevision = 1,
            CanonicalJson = "{}",
            ContentHash = new string('b', 64)
        };
        var question = new TheoryQuestionBankItem
        {
            Type = TheoryQuestionType.SingleChoice,
            BankName = " Network   Fundamentals ",
            Title = "CIDR",
            Options = ["A", "B"],
            AnswerIndexes = [0]
        };
        context.AddRange(node, template, topology, release, question);
        await context.SaveChangesAsync();

        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = release.Id,
            Status = TeamLabRuntimeStatus.Running,
            IsOpenToPlayers = true
        };
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();

        var distributionId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        const string references = "[{\"Kind\":0,\"Id\":7001},{\"Kind\":1,\"Id\":7002}]";
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "ImageDistributionRecords"
                ("Id", "ImageTemplateId", "WorkerNodeId", "ImageHash", "ImageType", "Status",
                 "ReferenceCount", "References", "CreatedAt")
            VALUES ({{distributionId}}, {{template.Id}}, {{node.Id}}, {{template.ImageHash!}}, 0, 2,
                    2, {{references}}, '2026-01-01T00:00:00Z');

            INSERT INTO "Logs" ("Id", "TimeUtc", "Level", "Logger", "Message") VALUES
                (910001, '2026-01-15T10:05:00Z', 'Information', 'Phase4.Migration', 'first'),
                (910002, '2026-02-15T11:05:00Z', 'Warning', 'Phase4.Migration', 'second');

            INSERT INTO "TeamLabTrafficFlows"
                ("Id", "RuntimeId", "Generation", "SourceCursor", "SourceIp", "SourcePort",
                 "DestinationIp", "DestinationPort", "Protocol", "Bytes", "Packets",
                 "FirstSeenAt", "LastSeenAt", "CapturedAt")
            VALUES
                (920001, {{runtime.Id}}, 1, 1, '10.10.1.5', 40000, '192.168.20.8', 80,
                 'TCP', 512, 1, '2026-01-10T00:01:00Z', '2026-01-10T00:01:00Z', '2026-01-10T00:01:00Z'),
                (920002, {{runtime.Id}}, 1, 2, '10.10.1.5', 40001, '192.168.20.9', 443,
                 'TCP', 1024, 1, '2026-01-11T00:02:00Z', '2026-01-11T00:02:00Z', '2026-01-11T00:02:00Z');

            SET session_replication_role = replica;
            INSERT INTO "Participations" ("Id", "Status", "Token", "GameId", "TeamId") VALUES
                (930001, 1, 'phase4-duplicate-a', 9300, 9300),
                (930002, 1, 'phase4-duplicate-b', 9300, 9300);
            SET session_replication_role = origin;
            """);

        return new SeedFacts(distributionId, question.Id, runtime.Id);
    }

    private async Task AssertMigrationContractAsync(SeedFacts seed)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        Assert.Equal('p', await ScalarAsync<char>(connection,
            "SELECT relkind FROM pg_class WHERE oid = '\"Logs\"'::regclass"));
        Assert.Equal('p', await ScalarAsync<char>(connection,
            "SELECT relkind FROM pg_class WHERE oid = '\"TeamLabTrafficFlows\"'::regclass"));
        Assert.Equal(2L, await ScalarAsync<long>(connection,
            "SELECT count(*) FROM \"ImageDistributionReferences\" WHERE \"DistributionRecordId\" = @id",
            new NpgsqlParameter("id", seed.DistributionId)));
        Assert.Equal(1L, await ScalarAsync<long>(connection,
            "SELECT count(*) FROM \"TheoryQuestionTagBindings\" WHERE \"QuestionId\" = @id",
            new NpgsqlParameter("id", seed.QuestionId)));
        Assert.Equal(1L, await ScalarAsync<long>(connection,
            "SELECT count(*) FROM \"TheoryQuestionTagBindings\" WHERE \"QuestionId\" = @id",
            new NpgsqlParameter("id", seed.LateQuestionId)));
        Assert.Equal("BANK:NETWORK FUNDAMENTALS", await ScalarAsync<string>(connection,
            "SELECT tag.\"NormalizedName\" FROM \"TheoryQuestionTags\" tag " +
            "JOIN \"TheoryQuestionTagBindings\" binding ON binding.\"TagId\" = tag.\"Id\" " +
            "WHERE binding.\"QuestionId\" = @id",
            new NpgsqlParameter("id", seed.QuestionId)));
        Assert.False(await ScalarAsync<bool>(connection, """
            SELECT EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_name = 'ImageDistributionRecords' AND column_name IN ('References', 'ReferenceCount'))
            """));
        Assert.Equal("bigint", await ScalarAsync<string>(connection, """
            SELECT data_type FROM information_schema.columns
            WHERE table_name = 'Logs' AND column_name = 'Id'
            """));

        var logPartitions = await StringsAsync(connection, """
            SELECT DISTINCT tableoid::regclass::text FROM "Logs" ORDER BY 1
            """);
        Assert.Contains("\"Logs_p202601\"", logPartitions);
        Assert.Contains("\"Logs_p202602\"", logPartitions);
        var flowPartitions = await StringsAsync(connection, """
            SELECT DISTINCT tableoid::regclass::text FROM "TeamLabTrafficFlows" ORDER BY 1
            """);
        Assert.Contains("\"TeamLabTrafficFlows_p20260110\"", flowPartitions);
        Assert.Contains("\"TeamLabTrafficFlows_p20260111\"", flowPartitions);

        var duplicate = new NpgsqlCommand("""
            INSERT INTO "ImageDistributionReferences"
                ("Id", "DistributionRecordId", "Kind", "ResourceId", "CreatedAt")
            VALUES (gen_random_uuid(), @record, 0, 7001, now())
            """, connection);
        duplicate.Parameters.AddWithValue("record", seed.DistributionId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    private async Task AssertOperationalGovernanceAsync(SeedFacts seed)
    {
        await using var context = CreateContext();
        var aggregation = new OperationalAggregationService(context);
        var logStart = DateTimeOffset.Parse("2026-01-15T10:00:00Z");
        await aggregation.AggregateSystemLogsAsync(logStart, logStart.AddHours(1), CancellationToken.None);
        await aggregation.AggregateSystemLogsAsync(logStart, logStart.AddHours(1), CancellationToken.None);
        Assert.Equal(1, await context.OperationalLogAggregates.CountAsync(item => item.BucketStart == logStart));
        Assert.Equal(1, await context.OperationalLogAggregates
            .Where(item => item.BucketStart == logStart).Select(item => item.Count).SingleAsync());

        var flowStart = DateTimeOffset.Parse("2026-01-10T00:00:00Z");
        await aggregation.AggregateTeamLabFlowsAsync(flowStart, flowStart.AddMinutes(5), CancellationToken.None);
        await aggregation.AggregateTeamLabFlowsAsync(flowStart, flowStart.AddMinutes(5), CancellationToken.None);
        var flowAggregate = await context.TeamLabTrafficFlowAggregates
            .SingleAsync(item => item.RuntimeId == seed.RuntimeId && item.BucketStart == flowStart);
        Assert.Equal(1, flowAggregate.FlowCount);
        Assert.Equal(512, flowAggregate.Bytes);

        await using var secondContext = CreateContext();
        var firstLease = await new PostgresGovernanceLease(context).TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(firstLease);
        Assert.Null(await new PostgresGovernanceLease(secondContext).TryAcquireAsync(CancellationToken.None));
        await firstLease!.DisposeAsync();
        await using var reacquired = await new PostgresGovernanceLease(secondContext)
            .TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(reacquired);

        var terminal = new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.GameContainer,
            Status = DeploymentQueueTicketStatus.Completed,
            ActiveIdentity = "phase4-terminal",
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-400)
        };
        var active = new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.GameContainer,
            Status = DeploymentQueueTicketStatus.Creating,
            ActiveIdentity = "phase4-active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-400)
        };
        context.DeploymentQueueTickets.AddRange(terminal, active);
        await context.SaveChangesAsync();
        var deleted = await new TerminalHistoryCleaner(context).CleanDeploymentTicketsAsync(
            DateTimeOffset.UtcNow.AddDays(-180), 100, CancellationToken.None);
        Assert.Equal(1, deleted);
        Assert.True(await context.DeploymentQueueTickets.AnyAsync(item => item.Id == active.Id));

        var catalog = new DataRetentionPolicyCatalog(Options.Create(new DataRetentionOptions()));
        var partitions = new PostgresPartitionManager(context, catalog, new DataGovernanceMetrics());
        await partitions.EnsureFuturePartitionsAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var partitionCutoff = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        Assert.Equal(0, (await partitions.DropExpiredPartitionsAsync(
            "system-log", partitionCutoff, "phase4-test", CancellationToken.None)).PartitionCount);

        var expiredPartitions = await partitions.GetExpiredPartitionsAsync(
            "system-log", partitionCutoff, CancellationToken.None);
        foreach (var partition in expiredPartitions)
        {
            var rows = await context.Logs.LongCountAsync(
                item => item.TimeUtc >= partition.Lower && item.TimeUtc < partition.Upper);
            context.DataGovernanceRuns.Add(new DataGovernanceRun
            {
                DataSet = "system-log",
                Operation = "aggregate-partition",
                Status = DataGovernanceRunStatus.Completed,
                LeaseOwner = "phase4-test",
                Cutoff = partition.Upper,
                RowsRead = rows,
                PartitionName = partition.Name,
                CompletedAt = DateTimeOffset.UtcNow
            });
        }
        await context.SaveChangesAsync();
        var dropResult = await partitions.DropExpiredPartitionsAsync(
            "system-log", partitionCutoff, "phase4-test", CancellationToken.None);
        Assert.True(dropResult.PartitionCount > 0);
        Assert.Equal(dropResult.RowsDeleted, await context.DataGovernanceRuns
            .Where(item => item.Operation == "drop-partition")
            .SumAsync(item => item.RowsDeleted));
        Assert.All(await context.DataGovernanceRuns
                .Where(item => item.Operation == "drop-partition").ToArrayAsync(),
            item => Assert.False(string.IsNullOrWhiteSpace(item.PartitionName)));
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string[]> StringsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private sealed record SeedFacts(Guid DistributionId, int QuestionId, int RuntimeId)
    {
        public int LateQuestionId { get; init; }
    }
}
