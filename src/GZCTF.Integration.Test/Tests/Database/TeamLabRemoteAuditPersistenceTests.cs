using GZCTF.Infrastructure.Concurrency;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Migrations;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public class TeamLabRemoteAuditPersistenceTests : IAsyncLifetime
{
    [Fact]
    public async Task DeviceObservationMigrationPreservesAssetsAndRollsBack()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "TeamLabRuntimeAssets" ("Id" integer PRIMARY KEY, "Status" smallint NOT NULL);
            INSERT INTO "TeamLabRuntimeAssets" VALUES (1, 5), (2, 8);
            """);
        var migration = new GZCTF.Migrations.TeamLabDeviceObservation();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM "TeamLabRuntimeAssets" WHERE "DeviceObservationJson" IS NULL AND "DeviceNextProbeAt" IS NULL
            """).SingleAsync());
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(new short[] { 5, 8 }, await db.Database.SqlQueryRaw<short>("""
            SELECT "Status" AS "Value" FROM "TeamLabRuntimeAssets" ORDER BY "Id"
            """).ToArrayAsync());
    }
    [Fact]
    public async Task LinkPolicyGenerationMigrationPreservesLegacyUncertaintyAndRollback()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "TeamLabLinkPolicies" ("Id" integer PRIMARY KEY, "Status" smallint NOT NULL);
            INSERT INTO "TeamLabLinkPolicies" VALUES (1, 1), (2, 3);
            """);
        var migration = new TeamLabLinkPolicyGeneration();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM "TeamLabLinkPolicies" WHERE "Generation" IS NULL
            """).SingleAsync());
        await db.Database.ExecuteSqlRawAsync("""UPDATE "TeamLabLinkPolicies" SET "Generation" = 3 WHERE "Id" = 2""");
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(new short[] { 1, 3 }, await db.Database.SqlQueryRaw<short>("""
            SELECT "Status" AS "Value" FROM "TeamLabLinkPolicies" ORDER BY "Id"
            """).ToArrayAsync());
    }

    [Fact]
    public async Task AssetPowerIntentMigrationPreservesExistingRowsAndSupportsRollback()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "TeamLabRuntimeAssets" ("Id" integer PRIMARY KEY, "Status" smallint NOT NULL);
            INSERT INTO "TeamLabRuntimeAssets" VALUES (1, 5), (2, 8);
            """);
        var migration = new TeamLabAssetPowerIntent();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var sftp = new TeamLabSftpHostIdentity();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(sftp.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM "TeamLabRuntimeAssets" WHERE "DesiredPowerState" IS NULL
            """).SingleAsync());
        await db.Database.ExecuteSqlRawAsync("""UPDATE "TeamLabRuntimeAssets" SET "DesiredPowerState" = 'stopped' WHERE "Id" = 1""");
        Assert.Equal("stopped", await db.Database.SqlQueryRaw<string>("""
            SELECT "DesiredPowerState" AS "Value" FROM "TeamLabRuntimeAssets" WHERE "Id" = 1
            """).SingleAsync());
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(sftp.DownOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(new short[] { 5, 8 }, await db.Database.SqlQueryRaw<short>("""
            SELECT "Status" AS "Value" FROM "TeamLabRuntimeAssets" ORDER BY "Id"
            """).ToArrayAsync());
    }

    [Fact]
    public async Task ProvisioningMigrationKeepsLegacyActiveVmCleanupConservative()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "TeamLabRemoteSessions" ("Id" bigint PRIMARY KEY, "Protocol" smallint NOT NULL, "Status" smallint NOT NULL);
            INSERT INTO "TeamLabRemoteSessions" VALUES (1, 1, 3), (2, 4, 1), (3, 3, 5);
            """);
        var migration = new TeamLabRemoteProvisioningPhase();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var states = await db.Database.SqlQueryRaw<bool>("""
            SELECT "GuacamoleCreationStarted" AS "Value" FROM "TeamLabRemoteSessions" ORDER BY "Id"
            """).ToArrayAsync();
        Assert.Equal(new[] { false, true, false }, states);
    }

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("teamlab_remote_audit").WithUsername("postgres").WithPassword("postgres").WithCleanUp(true).Build();
    private readonly string root = Path.Combine(Path.GetTempPath(), "teamlab-audit-test-" + Guid.NewGuid().ToString("N"));
    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync()
    {
        await postgres.DisposeAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public async Task EvidenceSurvivesMigrationAndIsRemovedAfterRetention()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.EnsureCreatedAsync();
        var actor = new UserInfo { UserName = "audit-fixture" };
        var node = new WorkerNode { Name = "audit-node", HostAddress = "127.0.0.1", AuthToken = "fixture" };
        var release = new TeamLabTopologyRelease { Topology = new TeamLabTopology { Name = "audit-scene" }, CanonicalJson = "{}", Version = 1, ContentHash = new string('a', 64) };
        var runtime = new TeamLabRuntime { TopologyReleaseId = release.Id, CreatedById = actor.Id, CreateRequestHash = "audit-fixture" };
        var asset = new TeamLabRuntimeAsset { Runtime = runtime, Name = "web", TopologyKey = "web" };
        var session = new TeamLabRemoteSession { Runtime = runtime, RuntimeAsset = asset, WorkerNode = node, RequestedBy = actor,
            RequestedByUserId = actor.Id, Generation = 1, Protocol = TeamLabRemoteProtocol.ContainerTerminal,
            Reason = "support investigation", Status = TeamLabRemoteSessionStatus.Ended, EndedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        db.Users.Add(actor);
        db.TeamLabTopologyReleases.Add(release);
        db.TeamLabRemoteSessions.Add(session);
        await db.SaveChangesAsync();
        var storage = new LocalBlobStorage(root);
        var audit = new TeamLabRemoteAuditService(db, storage, new TeamLabAuthorizationService(db, [], []),
            new LocalDevelopmentLeaseProvider(), Options.Create(new TeamLabRemoteAuditOptions()),
            new TeamLabEventRecorder(db, new EfOperationalEventWriter(db, NullLogger<EfOperationalEventWriter>.Instance), new OperationalCorrelation()),
            NullLogger<TeamLabRemoteAuditService>.Instance);
        await audit.GenerateAsync(session.PublicId, actor.Id, false, default);
        var original = await db.Set<TeamLabRemoteAuditFile>().AsNoTracking().SingleAsync();
        Assert.True(await storage.ExistsAsync(original.RelativePath));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"TeamLabRemoteAuditFiles\" DROP COLUMN \"ReadyAt\"");
        var migration = new TeamLabRemoteAuditReadiness();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        db.ChangeTracker.Clear();
        Assert.Null((await db.Set<TeamLabRemoteAuditFile>().AsNoTracking().SingleAsync()).ReadyAt);
        await audit.MaintainAsync(default);
        var ready = await db.Set<TeamLabRemoteAuditFile>().AsNoTracking().SingleAsync();
        Assert.NotNull(ready.ReadyAt);
        Assert.Equal(original.Sha256, ready.Sha256);
        var download = await audit.DownloadAsync(session.PublicId, ready.Id, actor.Id, false, default);
        Assert.Equal(ready.Size, download.Content.Length);
        var persisted = await db.TeamLabRemoteSessions.SingleAsync();
        persisted.EndedAt = DateTimeOffset.UtcNow.AddDays(-91);
        await db.SaveChangesAsync();
        await audit.MaintainAsync(default);
        Assert.False(await storage.ExistsAsync(ready.RelativePath));
        Assert.Empty(await db.Set<TeamLabRemoteAuditFile>().ToArrayAsync());
    }
}
