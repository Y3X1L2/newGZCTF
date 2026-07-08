using GZCTF.Controllers;
using GZCTF.Models.Data;
using GZCTF.Services.TeamLab;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Text.Json;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabAdminControllerTests
{
    [Fact]
    public void ToActionResult_ReturnsBadRequestForFailedPlan()
    {
        var result = TeamLabAdminController.ToActionResult(
            new TeamLabPlanResult(false, "planning failed", null));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<TeamLabPlanResult>(badRequest.Value);
        Assert.False(body.Success);
        Assert.Equal("planning failed", body.Message);
    }

    [Fact]
    public void ToActionResult_ReturnsBadRequestForFailedDeployment()
    {
        var result = TeamLabAdminController.ToActionResult(
            new TeamLabDeploymentResult(false, "deployment failed", new TeamLabRuntime()));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<TeamLabDeploymentResult>(badRequest.Value);
        Assert.False(body.Success);
        Assert.Equal("deployment failed", body.Message);
    }

    [Fact]
    public void ToActionResult_ReturnsAcceptedForQueuedDeployment()
    {
        var queue = new GZCTF.Services.Fleet.DeploymentQueueStatusModel(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            GZCTF.Models.Data.DeploymentQueueKind.TeamLabRuntime,
            GZCTF.Models.Data.DeploymentQueueTicketStatus.Pending,
            null,
            null,
            QueuePosition: 2,
            PeopleAhead: 1,
            ErrorMessage: null,
            DateTimeOffset.Parse("2026-07-06T00:00:00Z"),
            StartedAt: null,
            CompletedAt: null);

        var result = TeamLabAdminController.ToActionResult(
            new TeamLabDeploymentResult(false, "queued", new TeamLabRuntime(), queue));

        var accepted = Assert.IsType<AcceptedResult>(result);
        var body = Assert.IsType<TeamLabDeploymentResult>(accepted.Value);
        Assert.False(body.Success);
        Assert.Equal(1, body.Queue?.PeopleAhead);
    }

    [Fact]
    public void CreateDeployOperationToken_IsIndependentFromRequestAbortAndControllerTimeout()
    {
        Assert.Null(typeof(TeamLabAdminController).GetField("DeployOperationTimeout"));

        using var applicationStopping = new CancellationTokenSource();
        using var deployToken = TeamLabAdminController.CreateDeployOperationToken(applicationStopping.Token);

        Assert.False(deployToken.Token.IsCancellationRequested);

        applicationStopping.Cancel();
        Assert.True(deployToken.Token.IsCancellationRequested);
    }

    [Fact]
    public void TeamLabPlanResult_SerializesCycleFreeRuntimeSummary()
    {
        var result = new TeamLabPlanResult(true, "planned", new TeamLabRuntime
        {
            Id = 7,
            GameId = 28,
            TeamId = 29,
            PublishedVersion = 1,
            WorkerNodeId = Guid.Parse("02ec0080-77ef-4030-b075-4bce445ea2f3"),
            NetworkPrefix = "10.180.0.0/24",
            Status = TeamLabRuntimeStatus.Scheduled,
            PublicUdpMapping = new TeamLabPublicUdpMapping
            {
                PublicUdpPort = 32000,
                WorkerTunnelIp = "10.24.0.27",
                WorkerWireGuardPort = 42000
            }
        });

        var json = JsonSerializer.Serialize(result);

        Assert.Equal(32000, result.RuntimeModel?.PublicUdpMapping?.PublicUdpPort);
        Assert.Contains("\"runtime\"", json);
        Assert.DoesNotContain("\"Runtime\"", json);
        Assert.DoesNotContain("\"game\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TeamLabTrafficCaptureResult_SerializesSafeJobSummary()
    {
        var result = new TeamLabTrafficCaptureResult(true, "started", new TeamLabTrafficCaptureJob
        {
            Id = 13,
            RuntimeId = 7,
            ShardId = 11,
            WorkerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Status = TeamLabTrafficCaptureStatus.Running,
            Scope = "network:entry",
            FilePath = "/run/gzctf-teamlab/capture-7-13/capture.pcap",
            MaxSeconds = 120,
            MaxBytes = 16 * 1024 * 1024,
            CapturedBytes = 1024
        });

        var json = JsonSerializer.Serialize(result);

        Assert.NotNull(result.JobModel);
        Assert.Contains("\"job\"", json);
        Assert.Contains("\"capturedBytes\":1024", json);
        Assert.DoesNotContain("\"runtime\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"workerNode\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanReuseScheduledRuntime_RequiresCompletePlanForSamePublishedVersion()
    {
        var workerNodeId = Guid.NewGuid();
        var runtime = new TeamLabRuntime
        {
            Status = TeamLabRuntimeStatus.Scheduled,
            PublishedVersion = 2,
            WorkerNodeId = workerNodeId,
            NetworkPrefix = "10.180.0.0/24",
            PublicUdpMapping = new TeamLabPublicUdpMapping { PublicUdpPort = 32000 }
        };

        Assert.False(TeamLabPlanService.CanReuseScheduledRuntime(runtime, 2));

        var shard = new TeamLabRuntimeShard
        {
            WorkerNodeId = workerNodeId,
            Runtime = runtime
        };
        runtime.Shards.Add(shard);
        runtime.Networks.Add(new TeamLabRuntimeNetwork
        {
            TopologyKey = "entry",
            WorkerNodeId = workerNodeId,
            Shard = shard
        });
        runtime.Assets.Add(new TeamLabRuntimeAsset
        {
            Kind = TeamLabResourceKind.Docker,
            TopologyKey = "portal",
            WorkerNodeId = workerNodeId,
            Shard = shard
        });

        Assert.True(TeamLabPlanService.CanReuseScheduledRuntime(runtime, 2));
        Assert.False(TeamLabPlanService.CanReuseScheduledRuntime(runtime, 3));
    }
}
