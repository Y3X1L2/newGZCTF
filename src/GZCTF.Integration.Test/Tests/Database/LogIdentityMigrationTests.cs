using GZCTF.Migrations;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Admin;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Infrastructure.Observability;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using GZCTF.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class LogIdentityMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("log_identity").WithUsername("postgres").WithPassword("postgres").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task PartitionedLogs_UpgradePreservesUnknownHistoryAndBackupRestoresIdentity()
    {
        await using var context = Context(postgres.GetConnectionString());
        await context.Database.EnsureCreatedAsync();
        var migration = new AddLogIdentityAndOutcome();
        await ApplyAsync(context, migration.DownOperations);
        // Production Logs are partitioned; exercise propagation of the additive columns to children.
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Logs" RENAME TO "Logs_old";
            CREATE TABLE "Logs" (LIKE "Logs_old" INCLUDING ALL) PARTITION BY RANGE ("TimeUtc");
            CREATE TABLE "Logs_default" PARTITION OF "Logs" DEFAULT;
            DROP TABLE "Logs_old";
            INSERT INTO "Logs" ("TimeUtc", "Level", "Logger", "Message", "UserName", "Status")
            VALUES (now(), 'Information', 'old-writer', 'legacy-row', 'Anonymous', 'Success');
            """);
        await BackupAsync("before_upgrade");
        await ApplyAsync(context, migration.UpOperations);
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var longEvent = writer.Append(new OperationalEventDraft(OperationalEventCodes.Node.CapabilityChanged,
            OperationalEventOutcome.Observed, "bounded detail", WorkerNodeName: new string('n', 128),
            Detail: new Dictionary<string, object?> { ["reason"] = new string('r', 4080) }));
        Assert.InRange(longEvent.DetailJson!.Length, 4097, 8192);
        var legacy = await context.Logs.AsNoTracking().SingleAsync();
        Assert.Null(legacy.ActorKind);
        Assert.Null(legacy.ActorUserId);
        Assert.Null(legacy.RemoteIP);
        Assert.Null(legacy.Outcome);
        context.Logs.Add(new LogModel
        {
            TimeUtc = DateTimeOffset.UtcNow.AddSeconds(1), Level = "Information", Logger = "node-monitor",
            Message = "fresh-row", UserName = "System", ActorKind = "System", Outcome = "Observed",
            DetailJson = "{\"actorKind\":\"System\",\"operation\":\"node.poll\"}"
        });
        await context.SaveChangesAsync();
        await BackupAsync("after_upgrade");
        await RestoreAsync("after_upgrade", "log_restored");
        var connection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = "log_restored" };
        await using var restored = Context(connection.ConnectionString);
        var page = await new LogRepository(restored, new EfOperationalLogDisplayQuery(restored))
            .GetLogs(new LogQueryModel(), CancellationToken.None);
        var fresh = Assert.Single(page.Items, item => item.Msg == "fresh-row");
        Assert.Equal("System", fresh.ActorKind);
        Assert.Equal("Observed", fresh.Outcome);
        Assert.Null(fresh.Status);
        Assert.Equal("node.poll", fresh.Detail!["operation"].GetString());
        var old = Assert.Single(page.Items, item => item.Msg == "legacy-row");
        Assert.Equal("Unknown", old.ActorKind);
        Assert.Null(old.IP);
        Assert.Equal(longEvent.DetailJson, (await restored.OperationalEvents.SingleAsync()).DetailJson);
        // Restore the pre-upgrade backup, then reapply the forward migration as an actual recovery path.
        await RestoreAsync("before_upgrade", "log_pre_restored");
        connection.Database = "log_pre_restored";
        await using var previous = Context(connection.ConnectionString);
        await ApplyAsync(previous, migration.UpOperations);
        Assert.Equal("legacy-row", (await previous.Logs.SingleAsync()).Message);
        // Recovery uses the pre-upgrade backup. Down's old column limit cannot hold the larger snapshots.
        await context.OperationalEvents.ExecuteDeleteAsync();
        await ApplyAsync(context, migration.DownOperations);
        await ApplyAsync(context, migration.UpOperations);
        Assert.Equal(2, await context.Logs.CountAsync());
    }

    private static AppDbContext Context(string connection) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connection).Options);

    private static async Task ApplyAsync(AppDbContext context, IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
    {
        foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, context.Model))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
    }

    private async Task BackupAsync(string name)
    {
        var result = await postgres.ExecAsync(["pg_dump", "-U", "postgres", "-Fc", "-f", $"/tmp/{name}.dump", "log_identity"]);
        Assert.Equal(0, result.ExitCode);
    }

    private async Task RestoreAsync(string name, string database)
    {
        var created = await postgres.ExecAsync(["createdb", "-U", "postgres", database]);
        Assert.Equal(0, created.ExitCode);
        var result = await postgres.ExecAsync(["pg_restore", "-U", "postgres", "-d", database, $"/tmp/{name}.dump"]);
        Assert.Equal(0, result.ExitCode);
    }
}
