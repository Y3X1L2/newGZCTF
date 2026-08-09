using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.TeamLab.Api;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabCaptureTests
{
    [Fact]
    public async Task RuntimeScope_StartsSegmentsAcrossWorkerNodesInParallel()
    {
        await using var context = CreateContext();
        var runtime = SeedRuntime(context, out var firstNode, out var secondNode, out _, out _);
        await context.SaveChangesAsync();
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new Mock<ITeamLabNodeExecutor>(MockBehavior.Strict);
        executor.Setup(item => item.StartCaptureAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeCaptureStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, TeamLabNodeCaptureStartRequest request, CancellationToken token) =>
            {
                if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
                return new TeamLabNodeCaptureResult(
                    true, "started", request.SegmentId, 0, true, null, false);
            });
        var service = CreateTrafficService(context, executor.Object);

        var capture = await service.StartCaptureAsync(
            runtime.PublicId,
            new CreateTeamLabCaptureModel("runtime", null, 60, 1024 * 1024, 600),
            CancellationToken.None);

        Assert.Equal(TeamLabTrafficCaptureStatus.Running, capture.Status);
        Assert.Equal(2, capture.Segments.Count);
        Assert.Equal(2, entered);
        Assert.Equal(
            new[] { firstNode.Id, secondNode.Id }.Order(),
            (await context.TeamLabTrafficCaptureSegments.AsNoTracking()
                .Select(item => item.WorkerNodeId).ToArrayAsync()).Order());
        var budgets = await context.TeamLabTrafficCaptureSegments.AsNoTracking()
            .Select(item => item.MaxBytes)
            .ToArrayAsync();
        Assert.All(budgets, budget => Assert.True(budget > 0));
        Assert.Equal(1024 * 1024, budgets.Sum());
    }

    [Fact]
    public async Task InternalUpload_IsDigestVerifiedAndIdempotent()
    {
        using var files = new TemporaryDirectory();
        using var keys = new TemporaryDirectory();
        await using var context = CreateContext();
        var runtime = SeedRuntime(context, out var firstNode, out _, out var firstPoint, out var secondPoint);
        var valid = Encoding.UTF8.GetBytes("verified-pcap-segment");
        var tampered = Encoding.UTF8.GetBytes("tampered-pcap-segment");
        var digest = Convert.ToHexStringLower(SHA256.HashData(valid));
        var job = new TeamLabTrafficCaptureJob
        {
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            Status = TeamLabTrafficCaptureStatus.Stopping,
            Scope = "runtime",
            MaxBytes = 1024 * 1024,
            MaxSeconds = 60,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Segments =
            [
                Segment(firstNode.Id, firstPoint, valid.Length, digest),
                Segment(firstNode.Id, secondPoint, tampered.Length, digest)
            ]
        };
        context.TeamLabTrafficCaptureJobs.Add(job);
        await context.SaveChangesAsync();
        var artifacts = new TeamLabCaptureArtifactStore(new LocalBlobStorage(files.Path));
        var tokens = new TeamLabCaptureUploadTokenService(DataProtectionProvider.Create(keys.Path));

        var first = job.Segments[0];
        var firstToken = tokens.Issue(new TeamLabCaptureUploadGrant(
            job.PublicId, first.PublicId, first.WorkerNodeId, first.CapturedBytes, job.MaxBytes, digest),
            TimeSpan.FromMinutes(5));
        var firstResult = await UploadAsync(
            context, tokens, artifacts, job, first, firstToken, digest, valid);
        Assert.IsType<OkObjectResult>(firstResult);
        await context.Entry(first).ReloadAsync();
        Assert.Equal(TeamLabTrafficCaptureSegmentStatus.Uploaded, first.Status);
        Assert.NotNull(first.ObjectPath);
        Assert.True(await artifacts.ExistsAsync(first.ObjectPath!, CancellationToken.None));

        var duplicate = await UploadAsync(
            context, tokens, artifacts, job, first, firstToken, digest, valid);
        Assert.IsType<OkObjectResult>(duplicate);

        var second = job.Segments[1];
        var secondToken = tokens.Issue(new TeamLabCaptureUploadGrant(
            job.PublicId, second.PublicId, second.WorkerNodeId, second.CapturedBytes, job.MaxBytes, digest),
            TimeSpan.FromMinutes(5));
        var mismatch = await UploadAsync(
            context, tokens, artifacts, job, second, secondToken, digest, tampered);
        Assert.IsType<BadRequestObjectResult>(mismatch);
        await context.Entry(second).ReloadAsync();
        Assert.Equal(TeamLabTrafficCaptureSegmentStatus.Captured, second.Status);
        var mismatchPath = TeamLabCaptureArtifactStore.BuildObjectPath(
            runtime.PublicId, runtime.Generation, job.PublicId, second.PublicId);
        Assert.False(await artifacts.ExistsAsync(mismatchPath, CancellationToken.None));
    }

    [Fact]
    public void UploadToken_RejectsTamperingAndWrongShape()
    {
        using var keys = new TemporaryDirectory();
        var service = new TeamLabCaptureUploadTokenService(DataProtectionProvider.Create(keys.Path));
        var grant = new TeamLabCaptureUploadGrant(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1024, 2048, new string('a', 64));
        var token = service.Issue(grant, TimeSpan.FromMinutes(1));

        Assert.True(service.TryValidate(token, out var decoded));
        Assert.Equal(grant, decoded);
        Assert.False(service.TryValidate(token + "x", out _));
    }

    [Fact]
    public void CaptureUploadTokenLifetime_ScalesForLargeFilesWithBoundedLifetime()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), TeamLabCaptureCoordinator.CaptureUploadTokenLifetime(0));
        Assert.Equal(TimeSpan.FromMinutes(50),
            TeamLabCaptureCoordinator.CaptureUploadTokenLifetime(10L * 1024 * 1024 * 1024));
        Assert.Equal(TimeSpan.FromMinutes(120),
            TeamLabCaptureCoordinator.CaptureUploadTokenLifetime(100L * 1024 * 1024 * 1024));
    }

    [Fact]
    public async Task Archive_StreamsManifestAndEverySegment()
    {
        using var files = new TemporaryDirectory();
        var artifacts = new TeamLabCaptureArtifactStore(new LocalBlobStorage(files.Path));
        var runtimeId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("pcapng-data");
        var digest = Convert.ToHexStringLower(SHA256.HashData(content));
        var objectPath = TeamLabCaptureArtifactStore.BuildObjectPath(runtimeId, 3, captureId, segmentId);
        var write = await artifacts.WriteSegmentAsync(
            objectPath, new MemoryStream(content), content.Length, 1024, digest, CancellationToken.None);
        Assert.True(write.Success);
        var descriptor = new TeamLabCaptureArchiveDescriptor(
            runtimeId,
            3,
            captureId,
            "runtime",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10),
            [new TeamLabCaptureArchiveSegment(
                segmentId,
                observationId,
                TeamLabObservationPointKind.NetworkBridge,
                "entry",
                null,
                null,
                objectPath,
                content.Length,
                digest,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)]);
        await using var archive = new MemoryStream();

        await artifacts.WriteArchiveAsync(descriptor, archive, CancellationToken.None);

        archive.Position = 0;
        using var reader = new TarReader(archive, leaveOpen: true);
        var names = new List<string>();
        JsonDocument? manifest = null;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            names.Add(entry.Name);
            if (entry.Name != "manifest.json" || entry.DataStream is null) continue;
            manifest = await JsonDocument.ParseAsync(entry.DataStream);
        }
        Assert.Contains("manifest.json", names);
        Assert.Contains($"segments/0000-{segmentId:N}.pcapng", names);
        Assert.NotNull(manifest);
        Assert.Equal(captureId, manifest.RootElement.GetProperty("captureId").GetGuid());
        Assert.Equal(segmentId,
            manifest.RootElement.GetProperty("segments")[0].GetProperty("id").GetGuid());
        manifest.Dispose();
    }

    [Fact]
    public async Task ExpiredCapture_DeletesAgentAndObjectArtifactsBeforeFinalizing()
    {
        using var files = new TemporaryDirectory();
        using var keys = new TemporaryDirectory();
        await using var context = CreateContext();
        var runtime = SeedRuntime(context, out var firstNode, out _, out var firstPoint, out _);
        var content = Encoding.UTF8.GetBytes("expired-pcap-segment");
        var digest = Convert.ToHexStringLower(SHA256.HashData(content));
        var job = new TeamLabTrafficCaptureJob
        {
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            Status = TeamLabTrafficCaptureStatus.CleanupPending,
            Scope = "runtime",
            MaxBytes = 1024 * 1024,
            MaxSeconds = 60,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var segment = Segment(firstNode.Id, firstPoint, content.Length, digest);
        segment.Status = TeamLabTrafficCaptureSegmentStatus.CleanupPending;
        job.Segments.Add(segment);
        runtime.TrafficCaptureJobs.Add(job);
        context.TeamLabTrafficCaptureJobs.Add(job);
        await context.SaveChangesAsync();

        var artifacts = new TeamLabCaptureArtifactStore(new LocalBlobStorage(files.Path));
        var objectPath = TeamLabCaptureArtifactStore.BuildObjectPath(
            runtime.PublicId, runtime.Generation, job.PublicId, segment.PublicId);
        var write = await artifacts.WriteSegmentAsync(
            objectPath, new MemoryStream(content), content.Length, job.MaxBytes, digest, CancellationToken.None);
        Assert.True(write.Success);
        segment.ObjectPath = objectPath;
        await context.SaveChangesAsync();

        var executor = new Mock<ITeamLabNodeExecutor>(MockBehavior.Strict);
        executor.Setup(item => item.DeleteCaptureAsync(
                firstNode.Id,
                runtime.Id,
                runtime.Generation,
                job.PublicId,
                segment.PublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabNodeCaptureResult(
                true, "deleted", segment.PublicId, content.Length, false, digest, false));
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var coordinator = new TeamLabCaptureCoordinator(
            context,
            executor.Object,
            new TeamLabCaptureUploadTokenService(DataProtectionProvider.Create(keys.Path)),
            artifacts,
            new LocalDevelopmentLeaseProvider(),
            new TeamLabEventRecorder(context, writer, new OperationalCorrelation()),
            NullLogger<TeamLabCaptureCoordinator>.Instance);

        await coordinator.ProcessPendingAsync(CancellationToken.None);

        context.ChangeTracker.Clear();
        var expired = await context.TeamLabTrafficCaptureJobs
            .Include(item => item.Segments)
            .SingleAsync(item => item.Id == job.Id);
        Assert.Equal(TeamLabTrafficCaptureStatus.Expired, expired.Status);
        Assert.Equal(TeamLabTrafficCaptureSegmentStatus.Expired, expired.Segments.Single().Status);
        Assert.Null(expired.Segments.Single().ObjectPath);
        Assert.False(await artifacts.ExistsAsync(objectPath, CancellationToken.None));
        executor.VerifyAll();
    }

    private static async Task<IActionResult> UploadAsync(
        AppDbContext context,
        TeamLabCaptureUploadTokenService tokens,
        TeamLabCaptureArtifactStore artifacts,
        TeamLabTrafficCaptureJob job,
        TeamLabTrafficCaptureSegment segment,
        string token,
        string digest,
        byte[] body)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {token}";
        http.Request.Headers[InternalTeamLabCaptureUploadController.WorkerNodeHeader] =
            segment.WorkerNodeId.ToString("D");
        http.Request.Headers[InternalTeamLabCaptureUploadController.Sha256Header] = digest;
        http.Request.ContentLength = body.Length;
        http.Request.ContentType = "application/vnd.tcpdump.pcap";
        http.Request.Body = new MemoryStream(body);
        var controller = new InternalTeamLabCaptureUploadController(
            new TeamLabCaptureUploadService(
                context, tokens, artifacts, new LocalDevelopmentLeaseProvider()))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return await controller.Upload(job.PublicId, segment.PublicId, CancellationToken.None);
    }

    private static TeamLabTrafficCaptureSegment Segment(
        Guid workerNodeId,
        TeamLabObservationPoint point,
        long bytes,
        string digest) => new()
    {
        WorkerNodeId = workerNodeId,
        ObservationPoint = point,
        ObservationPointId = point.Id,
        Status = TeamLabTrafficCaptureSegmentStatus.Captured,
        CapturedBytes = bytes,
        Sha256 = digest,
        CompletedAt = DateTimeOffset.UtcNow
    };

    private static TeamLabRuntime SeedRuntime(
        AppDbContext context,
        out WorkerNode firstNode,
        out WorkerNode secondNode,
        out TeamLabObservationPoint firstPoint,
        out TeamLabObservationPoint secondPoint)
    {
        firstNode = new WorkerNode { Id = Guid.NewGuid(), Name = "capture-a", HostAddress = "10.0.0.1" };
        secondNode = new WorkerNode { Id = Guid.NewGuid(), Name = "capture-b", HostAddress = "10.0.0.2" };
        var runtime = new TeamLabRuntime
        {
            Id = 500,
            PublicId = Guid.NewGuid(),
            Generation = 3,
            Status = TeamLabRuntimeStatus.Running,
            CreateRequestHash = "capture-test"
        };
        var firstShard = new TeamLabRuntimeShard
        {
            Id = 501,
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            WorkerNode = firstNode,
            WorkerNodeId = firstNode.Id,
            Status = TeamLabRuntimeStatus.Running
        };
        var secondShard = new TeamLabRuntimeShard
        {
            Id = 502,
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            WorkerNode = secondNode,
            WorkerNodeId = secondNode.Id,
            Status = TeamLabRuntimeStatus.Running
        };
        var firstNetwork = Network(runtime, firstShard, firstNode, 503, "entry", "10.10.0.0/24");
        var secondNetwork = Network(runtime, secondShard, secondNode, 504, "core", "192.168.20.0/24");
        firstPoint = Observation(runtime, firstShard, firstNetwork, firstNode, 505);
        secondPoint = Observation(runtime, secondShard, secondNetwork, secondNode, 506);
        runtime.Shards.AddRange([firstShard, secondShard]);
        runtime.Networks.AddRange([firstNetwork, secondNetwork]);
        runtime.ObservationPoints.AddRange([firstPoint, secondPoint]);
        context.WorkerNodes.AddRange(firstNode, secondNode);
        context.TeamLabRuntimes.Add(runtime);
        return runtime;
    }

    private static TeamLabRuntimeNetwork Network(
        TeamLabRuntime runtime,
        TeamLabRuntimeShard shard,
        WorkerNode node,
        int id,
        string key,
        string cidr) => new()
    {
        Id = id,
        Runtime = runtime,
        RuntimeId = runtime.Id,
        Generation = runtime.Generation,
        Shard = shard,
        ShardId = shard.Id,
        WorkerNode = node,
        WorkerNodeId = node.Id,
        TopologyKey = key,
        Name = key,
        Cidr = cidr,
        GatewayIp = cidr.StartsWith("10.", StringComparison.Ordinal) ? "10.10.0.1" : "192.168.20.1",
        BridgeName = $"br-{key}"
    };

    private static TeamLabObservationPoint Observation(
        TeamLabRuntime runtime,
        TeamLabRuntimeShard shard,
        TeamLabRuntimeNetwork network,
        WorkerNode node,
        int id) => new()
    {
        Id = id,
        PublicId = Guid.NewGuid(),
        Runtime = runtime,
        RuntimeId = runtime.Id,
        Generation = runtime.Generation,
        Shard = shard,
        ShardId = shard.Id,
        Network = network,
        NetworkId = network.Id,
        WorkerNode = node,
        WorkerNodeId = node.Id,
        Kind = TeamLabObservationPointKind.NetworkBridge,
        TopologyKey = network.TopologyKey,
        InterfaceToken = network.BridgeName,
        Enabled = true
    };

    private static TeamLabTrafficApplicationService CreateTrafficService(
        AppDbContext context,
        ITeamLabNodeExecutor executor)
    {
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        return new TeamLabTrafficApplicationService(
            context,
            executor,
            new LocalDevelopmentLeaseProvider(),
            new Mock<ITeamLabTrafficIngestor>().Object,
            new TeamLabEventRecorder(context, writer, new OperationalCorrelation()),
            NullLogger<TeamLabTrafficApplicationService>.Instance);
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gzctf-capture-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
