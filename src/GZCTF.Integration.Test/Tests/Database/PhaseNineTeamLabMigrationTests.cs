using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class PhaseNineTeamLabMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260714021514_HardenExternalTeamLabApiContract";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_nine")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Backfill_PreservesV1ReleaseAndCreatesEquivalentRuntimeFacts()
    {
        var releaseId = Guid.Parse("99999999-9999-4999-8999-999999999991");
        var topologyPublicId = Guid.Parse("99999999-9999-4999-8999-999999999992");
        var runtimePublicId = Guid.Parse("99999999-9999-4999-8999-999999999993");
        var shardPublicId = Guid.Parse("99999999-9999-4999-8999-999999999994");
        var workerId = Guid.Parse("99999999-9999-4999-8999-999999999995");
        var capturePublicId = Guid.Parse("99999999-9999-4999-8999-999999999996");
        const string canonical = "{\"name\":\"v1-runtime\",\"networks\":[],\"assets\":[],\"connections\":[]}";
        const string editor = "{\"networks\":{},\"assets\":{}}";
        const string contentHash = "sha256:phase-nine-v1-identity";
        string storedCanonical;

        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "WorkerNodes"
                    ("Id", "Name", "HostAddress", "AuthToken", "Capabilities", "Status",
                     "CpuLoad", "MemoryLoad", "CurrentContainers", "MaxContainers",
                     "CurrentVms", "MaxVms", "UsedPorts", "TotalPorts", "RegisteredAt",
                     "IsLocal", "IsSchedulable", "TeamLabNetworkEnabled", "TeamLabTunnelStatus",
                     "TeamLabFabricStatus", "TeamLabTunnelIp")
                VALUES
                    ({{workerId}}, 'phase-nine-worker', '10.24.0.118', 'phase-nine-token',
                     {{(byte)NodeCapability.Docker}}, {{(byte)NodeStatus.Online}},
                     0, 0, 0, 8, 0, 0, 0, 0,
                     {{DateTimeOffset.Parse("2026-07-14T00:00:00Z")}},
                     true, true, true, {{(byte)TeamLabTunnelStatus.Healthy}},
                     {{(byte)TeamLabFabricStatus.Healthy}}, '10.251.0.18');

                INSERT INTO "TeamLabTopologies"
                    ("Id", "PublicId", "OwnerUserId", "Name", "Revision", "SchemaVersion",
                     "EditorMetadataJson", "CreatedByOperationId", "LastMutationOperationId",
                     "CreatedAt", "UpdatedAt")
                VALUES
                    (42, {{topologyPublicId}}, NULL, 'v1-runtime', 1, 1,
                     {{editor}}::jsonb, NULL, NULL,
                     {{DateTimeOffset.Parse("2026-07-14T00:00:00Z")}},
                     {{DateTimeOffset.Parse("2026-07-14T00:00:00Z")}});

                INSERT INTO "TeamLabTopologyReleases"
                    ("Id", "TopologyId", "Version", "SourceRevision", "SchemaVersion",
                     "CanonicalJson", "ContentHash", "PublishedById", "ApiOperationId", "PublishedAt")
                VALUES
                    ({{releaseId}}, 42, 1, 1, 1, {{canonical}}::jsonb, {{contentHash}}, NULL, NULL,
                     {{DateTimeOffset.Parse("2026-07-14T00:01:00Z")}});

                INSERT INTO "TeamLabTopologyConnections"
                    ("TopologyId", "Key", "FromNetworkKey", "ToNetworkKey", "ViaAssetKey")
                VALUES (42, 'entry-core', 'entry', 'core', 'router');

                INSERT INTO "TeamLabRuntimes"
                    ("Id", "PublicId", "TopologyReleaseId", "CreatedById", "Generation",
                     "ExternalReference", "CreateRequestHash", "EntryShardId", "Status",
                     "IsOpenToPlayers", "LastError", "CreatedAt", "UpdatedAt")
                VALUES
                    (42, {{runtimePublicId}}, {{releaseId}}, NULL, 1, NULL, 'sha256:runtime', NULL,
                     5, true, NULL, {{DateTimeOffset.Parse("2026-07-14T00:02:00Z")}},
                     {{DateTimeOffset.Parse("2026-07-14T00:03:00Z")}});

                INSERT INTO "TeamLabRuntimeShards"
                    ("Id", "PublicId", "RuntimeId", "Generation", "WorkerNodeId", "Status",
                     "RouteVersion", "LastError", "CreatedAt", "UpdatedAt")
                VALUES
                    (42, {{shardPublicId}}, 42, 1, {{workerId}}, 5, 1, NULL,
                     {{DateTimeOffset.Parse("2026-07-14T00:02:00Z")}},
                     {{DateTimeOffset.Parse("2026-07-14T00:03:00Z")}});

                UPDATE "TeamLabRuntimes" SET "EntryShardId" = 42 WHERE "Id" = 42;

                INSERT INTO "TeamLabRuntimeNetworks"
                    ("Id", "RuntimeId", "Generation", "NetworkLeaseId", "ShardId", "WorkerNodeId",
                     "PlacementGroupKey", "IsEntry", "TopologyKey", "Name", "Cidr", "GatewayIp",
                     "BridgeName", "FlowCursor")
                VALUES
                    (42, 42, 1, NULL, 42, {{workerId}}, 'entry', true, 'entry', 'Entry',
                     '10.20.0.0/24', '10.20.0.1', 'tl42-entry', 0);

                INSERT INTO "TeamLabTrafficCaptureJobs"
                    ("Id", "PublicId", "RuntimeId", "Generation", "ApiOperationId", "ShardId",
                     "NetworkId", "WorkerNodeId", "Status", "Scope", "FilePath", "MaxBytes",
                     "MaxSeconds", "CapturedBytes", "LastError", "CreatedAt", "StartedAt",
                     "CompletedAt", "ExpiresAt")
                VALUES
                    (42, {{capturePublicId}}, 42, 1, NULL, 42, 42, {{workerId}}, 3, 'network',
                     '/run/gzctf-teamlab/legacy-capture.pcap', 1048576, 300, 4096, NULL,
                     {{DateTimeOffset.Parse("2026-07-14T00:02:30Z")}},
                     {{DateTimeOffset.Parse("2026-07-14T00:02:31Z")}},
                     {{DateTimeOffset.Parse("2026-07-14T00:02:40Z")}},
                     {{DateTimeOffset.Parse("2026-07-15T00:02:40Z")}});
                """);
            storedCanonical = await context.TeamLabTopologyReleases.AsNoTracking()
                .Where(item => item.Id == releaseId)
                .Select(item => item.CanonicalJson)
                .SingleAsync();
            await migrator.MigrateAsync("20260714091420_BackfillPhaseNineTeamLabNetworking");
            Assert.Equal("/run/gzctf-teamlab/legacy-capture.pcap",
                await context.Database.SqlQueryRaw<string>("""
                    SELECT "FilePath" AS "Value"
                    FROM "TeamLabTrafficCaptureJobs"
                    WHERE "Id" = 42
                    """).SingleAsync());
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Contains("legacy capture files", blocked.MessageText, StringComparison.OrdinalIgnoreCase);
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE "TeamLabTrafficCaptureJobs"
                SET "FilePath" = NULL, "Status" = 5,
                    "LastError" = 'Legacy capture explicitly expired before Phase 9 contraction.'
                WHERE "Id" = 42
                """);
            await migrator.MigrateAsync();
        }

        await using var migrated = CreateContext();
        var release = await migrated.TeamLabTopologyReleases.AsNoTracking().SingleAsync(item => item.Id == releaseId);
        var infrastructure = await migrated.TeamLabRuntimeInfrastructures.AsNoTracking().SingleAsync();
        var fragment = await migrated.TeamLabRuntimeInfrastructureFragments.AsNoTracking().SingleAsync();
        var fabric = await migrated.TeamLabFabricLinkLeases.AsNoTracking().SingleAsync();
        var observations = await migrated.TeamLabObservationPoints.AsNoTracking().ToArrayAsync();
        var capture = await migrated.TeamLabTrafficCaptureJobs.AsNoTracking()
            .Include(item => item.Segments)
            .SingleAsync(item => item.PublicId == capturePublicId);
        var connection = await migrated.TeamLabTopologyConnections.AsNoTracking().SingleAsync();

        Assert.Equal(storedCanonical, release.CanonicalJson);
        Assert.Equal(contentHash, release.ContentHash);
        Assert.Equal("switch-entry", infrastructure.TopologyKey);
        Assert.Equal(42, fragment.ShardId);
        Assert.Equal("169.254.0.0/30", fabric.AllocatedCidr.ToString());
        Assert.Equal("169.254.0.1", fabric.HubAddress);
        Assert.Equal("169.254.0.2", fabric.NodeAddress);
        Assert.Equal(2, observations.Length);
        Assert.Equal("entry", capture.NetworkKey);
        Assert.Empty(capture.Segments);
        Assert.Equal(TeamLabTrafficCaptureStatus.Expired, capture.Status);
        Assert.Contains("explicitly expired", capture.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TeamLabConnectionDirection.Bidirectional, connection.Direction);
        Assert.Equal(0, await migrated.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'TeamLabTrafficCaptureJobs'
              AND column_name IN ('WorkerNodeId', 'ShardId', 'NetworkId', 'FilePath')
            """).SingleAsync());
        Assert.Empty(await migrated.Database.GetPendingMigrationsAsync());
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options) { SuppressProjectionRevisionBumps = true };
    }
}
