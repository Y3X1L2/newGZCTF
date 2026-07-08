using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabTrafficCaptureServiceTests
{
    [Fact]
    public async Task StartCaptureAsync_BindsJobToNetworkWorkerAndMarksRunning()
    {
        await using var context = CreateContext();
        var workerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedRuntimeAsync(context, workerNodeId);
        var agent = new RecordingCaptureAgentClient();
        var service = new TeamLabTrafficCaptureService(context, agent,
            NullLogger<TeamLabTrafficCaptureService>.Instance);

        var result = await service.StartCaptureAsync(gameId: 1, teamId: 2,
            new TeamLabCaptureStartModel(NetworkTopologyKey: "entry", ShardId: null, MaxSeconds: 120,
                MaxBytes: 16 * 1024 * 1024, RetentionSeconds: 3600), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Job);
        Assert.Equal(workerNodeId, result.Job!.WorkerNodeId);
        Assert.Equal(TeamLabTrafficCaptureStatus.Running, result.Job.Status);
        Assert.Equal("network:entry", result.Job.Scope);
        Assert.Equal("/run/gzctf-teamlab/capture-10-1/capture.pcap", result.Job.FilePath);
        Assert.Equal("tl10-entry", agent.StartRequests[0].Request.InterfaceName);
        Assert.Equal(workerNodeId, agent.StartRequests[0].NodeId);
        Assert.Contains(await context.TeamLabEvents.ToListAsync(), e =>
            e.Stage == "capture" && e.Level == TeamLabEventLevel.Success);
    }

    [Fact]
    public async Task StartCaptureAsync_FailsWhenNetworkHasNoWorker()
    {
        await using var context = CreateContext();
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            Id = 10,
            GameId = 1,
            TeamId = 2,
            Status = TeamLabRuntimeStatus.Running,
            Networks =
            [
                new TeamLabRuntimeNetwork
                {
                    TopologyKey = "entry",
                    Name = "Entry",
                    Cidr = "10.180.1.0/24",
                    BridgeName = "tl10-entry"
                }
            ]
        });
        await context.SaveChangesAsync();
        var service = new TeamLabTrafficCaptureService(context, new RecordingCaptureAgentClient(),
            NullLogger<TeamLabTrafficCaptureService>.Instance);

        var result = await service.StartCaptureAsync(gameId: 1, teamId: 2,
            new TeamLabCaptureStartModel(NetworkTopologyKey: "entry", ShardId: null, MaxSeconds: 120,
                MaxBytes: 16 * 1024 * 1024, RetentionSeconds: 3600), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("WorkerNode", result.Message);
        Assert.Null(result.Job);
    }

    [Fact]
    public async Task StopCaptureAsync_UpdatesCompletedStateFromAgent()
    {
        await using var context = CreateContext();
        var workerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedRuntimeAsync(context, workerNodeId);
        context.TeamLabTrafficCaptureJobs.Add(new TeamLabTrafficCaptureJob
        {
            Id = 9,
            RuntimeId = 10,
            WorkerNodeId = workerNodeId,
            Status = TeamLabTrafficCaptureStatus.Running,
            Scope = "network:entry",
            MaxSeconds = 120,
            MaxBytes = 16 * 1024 * 1024,
            FilePath = "/run/gzctf-teamlab/capture-10-9/capture.pcap"
        });
        await context.SaveChangesAsync();
        var agent = new RecordingCaptureAgentClient
        {
            StopResponse = new TeamLabCaptureResponse(true, false, "stopped",
                "/run/gzctf-teamlab/capture-10-9/capture.pcap", 4096, [])
        };
        var service = new TeamLabTrafficCaptureService(context, agent,
            NullLogger<TeamLabTrafficCaptureService>.Instance);

        var result = await service.StopCaptureAsync(gameId: 1, teamId: 2, jobId: 9, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(TeamLabTrafficCaptureStatus.Completed, result.Job?.Status);
        Assert.Equal(4096, result.Job?.CapturedBytes);
        Assert.Equal(workerNodeId, agent.StopRequests[0].NodeId);
        Assert.Contains(await context.TeamLabEvents.ToListAsync(), e =>
            e.Stage == "capture" && e.Level == TeamLabEventLevel.Success);
    }

    [Fact]
    public async Task RefreshStatusAsync_MarksExpiredRunningJob()
    {
        await using var context = CreateContext();
        var workerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedRuntimeAsync(context, workerNodeId);
        context.TeamLabTrafficCaptureJobs.Add(new TeamLabTrafficCaptureJob
        {
            Id = 9,
            RuntimeId = 10,
            WorkerNodeId = workerNodeId,
            Status = TeamLabTrafficCaptureStatus.Running,
            Scope = "network:entry",
            MaxSeconds = 120,
            MaxBytes = 16 * 1024 * 1024,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await context.SaveChangesAsync();
        var service = new TeamLabTrafficCaptureService(context, new RecordingCaptureAgentClient(),
            NullLogger<TeamLabTrafficCaptureService>.Instance);

        var result = await service.RefreshCaptureStatusAsync(gameId: 1, teamId: 2, jobId: 9, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(TeamLabTrafficCaptureStatus.Expired, result.Job?.Status);
    }

    [Fact]
    public async Task DownloadCaptureAsync_StreamsPcapFromOwningWorkerNode()
    {
        await using var context = CreateContext();
        var workerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedRuntimeAsync(context, workerNodeId);
        context.TeamLabTrafficCaptureJobs.Add(new TeamLabTrafficCaptureJob
        {
            Id = 9,
            RuntimeId = 10,
            WorkerNodeId = workerNodeId,
            Status = TeamLabTrafficCaptureStatus.Completed,
            Scope = "network:entry",
            MaxSeconds = 120,
            MaxBytes = 16 * 1024 * 1024,
            CapturedBytes = 4,
            FilePath = "/run/gzctf-teamlab/capture-10-9/capture.pcap"
        });
        await context.SaveChangesAsync();
        var agent = new RecordingCaptureAgentClient
        {
            DownloadResponse = TeamLabCaptureDownloadResult.FromStream(
                new MemoryStream([0xd4, 0xc3, 0xb2, 0xa1]),
                "capture-10-9.pcap",
                "application/vnd.tcpdump.pcap",
                4,
                null)
        };
        var service = new TeamLabTrafficCaptureService(context, agent,
            NullLogger<TeamLabTrafficCaptureService>.Instance);

        var result = await service.DownloadCaptureAsync(gameId: 1, teamId: 2, jobId: 9, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Stream);
        Assert.Equal("teamlab-game-1-team-2-capture-9.pcap", result.FileName);
        Assert.Equal("application/vnd.tcpdump.pcap", result.ContentType);
        Assert.Equal(4, result.Length);
        Assert.Equal(workerNodeId, agent.DownloadRequests[0].NodeId);
        Assert.Equal(10, agent.DownloadRequests[0].RuntimeId);
        Assert.Equal(9, agent.DownloadRequests[0].JobId);
    }

    [Fact]
    public async Task DownloadCaptureAsync_RejectsJobWithoutWorkerNode()
    {
        await using var context = CreateContext();
        await SeedRuntimeAsync(context, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        context.TeamLabTrafficCaptureJobs.Add(new TeamLabTrafficCaptureJob
        {
            Id = 9,
            RuntimeId = 10,
            WorkerNodeId = null,
            Status = TeamLabTrafficCaptureStatus.Completed,
            Scope = "network:entry",
            MaxSeconds = 120,
            MaxBytes = 16 * 1024 * 1024
        });
        await context.SaveChangesAsync();
        var agent = new RecordingCaptureAgentClient();
        var service = new TeamLabTrafficCaptureService(context, agent,
            NullLogger<TeamLabTrafficCaptureService>.Instance);

        var result = await service.DownloadCaptureAsync(gameId: 1, teamId: 2, jobId: 9, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("WorkerNode", result.Message);
        Assert.Empty(agent.DownloadRequests);
    }

    [Fact]
    public async Task RefreshRuntimeAsync_ImportsFlowMetadataSamples()
    {
        await using var context = CreateContext();
        var workerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedRuntimeAsync(context, workerNodeId);
        var capturedAt = DateTimeOffset.Parse("2026-07-07T10:11:12Z");
        var agent = new RecordingCaptureAgentClient
        {
            FlowSnapshotResponse = new TeamLabFlowResponse(true, false, "samples",
            [
                new TeamLabFlowSample(capturedAt, "10.180.1.10", 43122, "192.168.80.10", 80, "TCP", 1460)
            ], [])
        };
        var service = new TeamLabTrafficFlowService(context, agent,
            NullLogger<TeamLabTrafficFlowService>.Instance);

        var result = await service.RefreshRuntimeAsync(gameId: 1, teamId: 2, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.ImportedCount);
        var flow = await context.TeamLabTrafficFlows.SingleAsync();
        Assert.Equal(workerNodeId, flow.WorkerNodeId);
        Assert.Equal("10.180.1.10", flow.SourceIp);
        Assert.Equal(43122, flow.SourcePort);
        Assert.Equal("192.168.80.10", flow.DestinationIp);
        Assert.Equal(80, flow.DestinationPort);
        Assert.Equal("TCP", flow.Protocol);
        Assert.Equal(1460, flow.Bytes);
        Assert.Equal(workerNodeId, agent.FlowSnapshotRequests[0].NodeId);
        Assert.Equal("entry", agent.FlowSnapshotRequests[0].Request.NetworkKey);
    }

    [Fact]
    public async Task StartCollectorsAsync_StartsOneCollectorPerNetwork()
    {
        await using var context = CreateContext();
        var workerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedRuntimeAsync(context, workerNodeId);
        var runtime = await context.TeamLabRuntimes.Include(r => r.Networks).Include(r => r.Events).SingleAsync();
        var agent = new RecordingCaptureAgentClient();
        var service = new TeamLabTrafficFlowService(context, agent,
            NullLogger<TeamLabTrafficFlowService>.Instance);

        var result = await service.StartCollectorsAsync(runtime, runtime.Networks, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var request = Assert.Single(agent.FlowStartRequests);
        Assert.Equal(workerNodeId, request.NodeId);
        Assert.Equal("entry", request.Request.NetworkKey);
        Assert.Equal("tl10-entry", request.Request.InterfaceName);
        Assert.Contains(runtime.Events, e => e.Stage == "traffic" && e.Level == TeamLabEventLevel.Success);
    }

    [Fact]
    public async Task StopCollectorsAsync_ReportsNetworkCleanupFailures()
    {
        await using var context = CreateContext();
        var workerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedRuntimeAsync(context, workerNodeId);
        var runtime = await context.TeamLabRuntimes.Include(r => r.Networks).SingleAsync();
        var agent = new RecordingCaptureAgentClient
        {
            FlowStopResponse = new TeamLabFlowResponse(false, false, "stop failed", [], [])
        };
        var service = new TeamLabTrafficFlowService(context, agent,
            NullLogger<TeamLabTrafficFlowService>.Instance);

        var result = await service.StopCollectorsAsync(runtime, runtime.Networks, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("entry", result.Message);
        Assert.Contains("stop failed", result.Message);
        Assert.Single(agent.FlowStopRequests);
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    static async Task SeedRuntimeAsync(AppDbContext context, Guid workerNodeId)
    {
        context.WorkerNodes.Add(new WorkerNode
        {
            Id = workerNodeId,
            Name = "node-a",
            HostAddress = "10.24.0.30",
            AuthToken = "token",
            Status = NodeStatus.Online,
            TeamLabNetworkEnabled = true
        });
        var shard = new TeamLabRuntimeShard
        {
            Id = 20,
            RuntimeId = 10,
            WorkerNodeId = workerNodeId,
            Status = TeamLabRuntimeStatus.Running
        };
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            Id = 10,
            GameId = 1,
            TeamId = 2,
            WorkerNodeId = workerNodeId,
            Status = TeamLabRuntimeStatus.Running,
            Shards = [shard],
            Networks =
            [
                new TeamLabRuntimeNetwork
                {
                    RuntimeId = 10,
                    Shard = shard,
                    WorkerNodeId = workerNodeId,
                    TopologyKey = "entry",
                    Name = "Entry",
                    Cidr = "10.180.1.0/24",
                    GatewayIp = "10.180.1.1",
                    BridgeName = "tl10-entry"
                }
            ]
        });
        await context.SaveChangesAsync();
    }

    sealed class RecordingCaptureAgentClient : AgentClient
    {
        public RecordingCaptureAgentClient()
            : base(new StaticHttpClientFactory(),
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new ConfigurationBuilder().Build(),
                NullLogger<AgentClient>.Instance)
        {
        }

        public List<(Guid NodeId, TeamLabCaptureStartRequest Request)> StartRequests { get; } = [];
        public List<(Guid NodeId, TeamLabCaptureStopRequest Request)> StopRequests { get; } = [];
        public List<(Guid NodeId, TeamLabCaptureStatusRequest Request)> StatusRequests { get; } = [];
        public List<(Guid NodeId, int RuntimeId, int JobId)> DownloadRequests { get; } = [];
        public List<(Guid NodeId, TeamLabFlowStartRequest Request)> FlowStartRequests { get; } = [];
        public List<(Guid NodeId, TeamLabFlowStopRequest Request)> FlowStopRequests { get; } = [];
        public List<(Guid NodeId, TeamLabFlowSnapshotRequest Request)> FlowSnapshotRequests { get; } = [];

        public TeamLabCaptureResponse? StartResponse { get; set; }
        public TeamLabCaptureResponse? StopResponse { get; set; }
        public TeamLabCaptureResponse? StatusResponse { get; set; }
        public TeamLabCaptureDownloadResult? DownloadResponse { get; set; }
        public TeamLabFlowResponse? FlowStartResponse { get; set; }
        public TeamLabFlowResponse? FlowStopResponse { get; set; }
        public TeamLabFlowResponse? FlowSnapshotResponse { get; set; }

        public override Task<TeamLabCaptureResponse?> StartTeamLabCaptureAsync(Guid nodeId,
            TeamLabCaptureStartRequest request, CancellationToken token)
        {
            StartRequests.Add((nodeId, request));
            return Task.FromResult<TeamLabCaptureResponse?>(StartResponse ??
                new TeamLabCaptureResponse(true, false, "started",
                    $"/run/gzctf-teamlab/capture-{request.RuntimeId}-{request.JobId}/capture.pcap", 0, []));
        }

        public override Task<TeamLabCaptureResponse?> StopTeamLabCaptureAsync(Guid nodeId,
            TeamLabCaptureStopRequest request, CancellationToken token)
        {
            StopRequests.Add((nodeId, request));
            return Task.FromResult<TeamLabCaptureResponse?>(StopResponse ??
                new TeamLabCaptureResponse(true, false, "stopped",
                    $"/run/gzctf-teamlab/capture-{request.RuntimeId}-{request.JobId}/capture.pcap", 0, []));
        }

        public override Task<TeamLabCaptureResponse?> GetTeamLabCaptureStatusAsync(Guid nodeId,
            TeamLabCaptureStatusRequest request, CancellationToken token)
        {
            StatusRequests.Add((nodeId, request));
            return Task.FromResult<TeamLabCaptureResponse?>(StatusResponse ??
                new TeamLabCaptureResponse(true, false, "running",
                    $"/run/gzctf-teamlab/capture-{request.RuntimeId}-{request.JobId}/capture.pcap", 1024, []));
        }

        public override Task<TeamLabCaptureDownloadResult?> DownloadTeamLabCaptureAsync(Guid nodeId,
            int runtimeId, int jobId, CancellationToken token)
        {
            DownloadRequests.Add((nodeId, runtimeId, jobId));
            return Task.FromResult<TeamLabCaptureDownloadResult?>(DownloadResponse);
        }

        public override Task<TeamLabFlowResponse?> StartTeamLabFlowMetadataAsync(Guid nodeId,
            TeamLabFlowStartRequest request, CancellationToken token)
        {
            FlowStartRequests.Add((nodeId, request));
            return Task.FromResult<TeamLabFlowResponse?>(FlowStartResponse ??
                new TeamLabFlowResponse(true, false, "started", [], []));
        }

        public override Task<TeamLabFlowResponse?> StopTeamLabFlowMetadataAsync(Guid nodeId,
            TeamLabFlowStopRequest request, CancellationToken token)
        {
            FlowStopRequests.Add((nodeId, request));
            return Task.FromResult<TeamLabFlowResponse?>(FlowStopResponse ??
                new TeamLabFlowResponse(true, false, "stopped", [], []));
        }

        public override Task<TeamLabFlowResponse?> GetTeamLabFlowMetadataSnapshotAsync(Guid nodeId,
            TeamLabFlowSnapshotRequest request, CancellationToken token)
        {
            FlowSnapshotRequests.Add((nodeId, request));
            return Task.FromResult<TeamLabFlowResponse?>(FlowSnapshotResponse ??
                new TeamLabFlowResponse(true, false, "empty", [], []));
        }
    }

    sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
