using GZCTF.Infrastructure.Concurrency;
using GZCTF.Infrastructure.Persistence.Governance;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabTrafficPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_teamlab_traffic")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ObservationBatch_IsIdempotent_CorrelatesExactPath_AndRetainsPathSnapshot()
    {
        var seed = await SeedAsync();
        var capturedAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z");
        var packetFingerprint = "sha256:" + new string('b', 64);
        var flowFingerprint = "sha256:" + new string('a', 64);
        var envelopes = new[]
        {
            Envelope(seed, seed.FirstPointId, 1, capturedAt, packetFingerprint, flowFingerprint),
            Envelope(seed, seed.SecondPointId, 2, capturedAt.AddMilliseconds(2), packetFingerprint, flowFingerprint)
        };

        await using (var writeContext = CreateContext())
        {
            var writer = new PostgresTeamLabTrafficBatchWriter(
                writeContext, NullLogger<PostgresTeamLabTrafficBatchWriter>.Instance);
            Assert.Equal(2, await writer.WriteAsync(envelopes, CancellationToken.None));
            Assert.Equal(0, await writer.WriteAsync(envelopes, CancellationToken.None));
        }

        await using (var correlationContext = CreateContext())
        {
            var writer = new EfOperationalEventWriter(
                correlationContext, NullLogger<EfOperationalEventWriter>.Instance);
            var correlator = new TeamLabTrafficPathCorrelator(
                correlationContext,
                new LocalDevelopmentLeaseProvider(),
                new TeamLabEventRecorder(
                    correlationContext, writer, new OperationalCorrelation()),
                NullLogger<TeamLabTrafficPathCorrelator>.Instance);
            Assert.Equal(1, await correlator.CorrelatePendingAsync(CancellationToken.None));
            Assert.Equal(0, await correlator.CorrelatePendingAsync(CancellationToken.None));
        }

        await using (var verifyContext = CreateContext())
        {
            var path = await verifyContext.TeamLabTrafficPaths.Include(item => item.Hops).SingleAsync();
            Assert.Equal(TeamLabPathConfidence.PacketExact, path.Confidence);
            Assert.Equal(2, path.Hops.Count);
            var cleaner = new TerminalHistoryCleaner(verifyContext);
            Assert.Equal(2, await cleaner.CleanTeamLabObservationsAsync(
                capturedAt.AddMinutes(1), 100, CancellationToken.None));
        }

        await using var retainedContext = CreateContext();
        Assert.Empty(await retainedContext.TeamLabTrafficObservations.ToArrayAsync());
        var retained = await retainedContext.TeamLabTrafficPaths.Include(item => item.Hops).SingleAsync();
        Assert.Equal(2, retained.Hops.Count);
        Assert.All(retained.Hops, hop => Assert.Null(hop.ObservationId));
        Assert.Equal([seed.FirstPointId, seed.SecondPointId],
            retained.Hops.OrderBy(item => item.Ordinal).Select(item => item.ObservationPointId).ToArray());
    }

    private async Task<Seed> SeedAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var worker = new WorkerNode
        {
            Name = "traffic-worker",
            HostAddress = "10.24.0.118",
            AuthToken = "traffic-test-token",
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online
        };
        var topology = new TeamLabTopology { Name = "traffic-topology" };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology,
            Version = 1,
            SourceRevision = 1,
            SchemaVersion = 2,
            CanonicalJson = "{}",
            ContentHash = "sha256:" + new string('c', 64)
        };
        context.AddRange(worker, release);
        await context.SaveChangesAsync();
        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = release.Id,
            Status = TeamLabRuntimeStatus.Running,
            CreateRequestHash = "sha256:" + new string('d', 64)
        };
        context.Add(runtime);
        await context.SaveChangesAsync();
        var shard = new TeamLabRuntimeShard
        {
            RuntimeId = runtime.Id,
            WorkerNodeId = worker.Id,
            Status = TeamLabRuntimeStatus.Running
        };
        context.Add(shard);
        await context.SaveChangesAsync();
        var network = new TeamLabRuntimeNetwork
        {
            RuntimeId = runtime.Id,
            ShardId = shard.Id,
            WorkerNodeId = worker.Id,
            PlacementGroupKey = "entry",
            IsEntry = true,
            TopologyKey = "entry",
            Name = "Entry",
            Cidr = "10.10.0.0/24",
            GatewayIp = "10.10.0.1",
            BridgeName = "tl-entry"
        };
        context.Add(network);
        await context.SaveChangesAsync();
        var points = new[]
        {
            new TeamLabObservationPoint
            {
                RuntimeId = runtime.Id,
                WorkerNodeId = worker.Id,
                ShardId = shard.Id,
                NetworkId = network.Id,
                Kind = TeamLabObservationPointKind.NetworkBridge,
                TopologyKey = "entry",
                InterfaceToken = "tl-entry"
            },
            new TeamLabObservationPoint
            {
                RuntimeId = runtime.Id,
                WorkerNodeId = worker.Id,
                ShardId = shard.Id,
                NetworkId = network.Id,
                Kind = TeamLabObservationPointKind.RouterFragment,
                TopologyKey = "router",
                InterfaceToken = "tl-router"
            }
        };
        context.AddRange(points);
        await context.SaveChangesAsync();
        return new Seed(runtime.Id, worker.Id, shard.Id, network.Id, points[0].Id, points[1].Id);
    }

    private static TeamLabTrafficEnvelope Envelope(
        Seed seed,
        int observationPointId,
        long sequence,
        DateTimeOffset capturedAt,
        string packetFingerprint,
        string flowFingerprint) => TeamLabTrafficEnvelope.Create(
        seed.RuntimeId,
        1,
        seed.ShardId,
        seed.NetworkId,
        observationPointId,
        observationPointId == seed.FirstPointId
            ? TeamLabObservationPointKind.NetworkBridge
            : TeamLabObservationPointKind.RouterFragment,
        null,
        seed.WorkerNodeId,
        new TeamLabNodeObservationRecord(
            sequence,
            Guid.NewGuid(),
            null,
            capturedAt,
            "10.10.0.2",
            42000,
            "10.20.0.3",
            443,
            "TCP",
            0x18,
            128,
            packetFingerprint,
            flowFingerprint,
            "Packet",
            null,
            "observed"));

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options) { SuppressProjectionRevisionBumps = true };
    }

    private sealed record Seed(
        int RuntimeId,
        Guid WorkerNodeId,
        int ShardId,
        int NetworkId,
        int FirstPointId,
        int SecondPointId);
}
