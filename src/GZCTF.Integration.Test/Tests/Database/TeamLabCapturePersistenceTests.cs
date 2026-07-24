using System.Security.Cryptography;
using System.Text;
using GZCTF.Infrastructure.Persistence.Governance;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Storage;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabCapturePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_teamlab_capture")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();
    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(), $"gzctf-capture-integration-{Guid.NewGuid():N}");

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, true);
    }

    [Fact]
    public async Task UploadedSegment_IsPersistedAndExpiryDeletesObject()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "capture-worker",
            HostAddress = "10.0.0.10",
            AuthToken = "capture-worker-token"
        };
        var topology = new TeamLabTopology { Name = "capture-topology" };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology,
            Version = 1,
            SourceRevision = 1,
            SchemaVersion = 2,
            CanonicalJson = "{}",
            ContentHash = "sha256:" + new string('c', 64)
        };
        var runtime = new TeamLabRuntime
        {
            PublicId = Guid.NewGuid(),
            TopologyReleaseId = release.Id,
            Generation = 1,
            Status = TeamLabRuntimeStatus.Running,
            CreateRequestHash = "capture-integration"
        };
        var shard = new TeamLabRuntimeShard
        {
            Runtime = runtime,
            Generation = runtime.Generation,
            WorkerNode = node,
            WorkerNodeId = node.Id,
            Status = TeamLabRuntimeStatus.Running
        };
        var network = new TeamLabRuntimeNetwork
        {
            Runtime = runtime,
            Generation = runtime.Generation,
            Shard = shard,
            WorkerNode = node,
            WorkerNodeId = node.Id,
            TopologyKey = "entry",
            Name = "entry",
            Cidr = "10.10.0.0/24",
            GatewayIp = "10.10.0.1",
            BridgeName = "br-entry"
        };
        var point = new TeamLabObservationPoint
        {
            Runtime = runtime,
            Generation = runtime.Generation,
            Shard = shard,
            Network = network,
            WorkerNode = node,
            WorkerNodeId = node.Id,
            Kind = TeamLabObservationPointKind.NetworkBridge,
            TopologyKey = network.TopologyKey,
            InterfaceToken = network.BridgeName
        };
        var content = Encoding.UTF8.GetBytes("integration-pcapng");
        var digest = Convert.ToHexStringLower(SHA256.HashData(content));
        var job = new TeamLabTrafficCaptureJob
        {
            Runtime = runtime,
            Generation = runtime.Generation,
            Status = TeamLabTrafficCaptureStatus.Completed,
            Scope = "runtime",
            MaxBytes = 1024,
            MaxSeconds = 60,
            CapturedBytes = content.Length,
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var segment = new TeamLabTrafficCaptureSegment
        {
            CaptureJob = job,
            WorkerNode = node,
            WorkerNodeId = node.Id,
            ObservationPoint = point,
            Status = TeamLabTrafficCaptureSegmentStatus.Uploaded,
            CapturedBytes = content.Length,
            UploadedBytes = content.Length,
            Sha256 = digest,
            CompletedAt = job.CompletedAt,
            UploadedAt = job.CompletedAt
        };
        runtime.Shards.Add(shard);
        runtime.Networks.Add(network);
        runtime.ObservationPoints.Add(point);
        runtime.TrafficCaptureJobs.Add(job);
        job.Segments.Add(segment);
        context.AddRange(node, release);
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();

        var artifacts = new TeamLabCaptureArtifactStore(new LocalBlobStorage(_storageRoot));
        var objectPath = TeamLabCaptureArtifactStore.BuildObjectPath(
            runtime.PublicId, runtime.Generation, job.PublicId, segment.PublicId);
        var write = await artifacts.WriteSegmentAsync(
            objectPath, new MemoryStream(content), content.Length, job.MaxBytes, digest, CancellationToken.None);
        Assert.True(write.Success);
        segment.ObjectPath = objectPath;
        await context.SaveChangesAsync();

        var cleaner = new TerminalHistoryCleaner(context);
        Assert.Equal(1, await cleaner.CleanExpiredTeamLabCaptureArtifactsAsync(
            DateTimeOffset.UtcNow, 100, CancellationToken.None));

        context.ChangeTracker.Clear();
        var retained = await context.TeamLabTrafficCaptureJobs.Include(item => item.Segments).SingleAsync();
        Assert.Equal(TeamLabTrafficCaptureStatus.CleanupPending, retained.Status);
        Assert.Equal(TeamLabTrafficCaptureSegmentStatus.CleanupPending, retained.Segments.Single().Status);
        Assert.Equal(objectPath, retained.Segments.Single().ObjectPath);
        Assert.True(await artifacts.ExistsAsync(objectPath, CancellationToken.None));
    }

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
