using GZCTF.Controllers;
using GZCTF.Models.Data;
using GZCTF.Services.TeamLab;
using Microsoft.AspNetCore.Mvc;
using System;
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
    public void CanReuseScheduledRuntime_RequiresCompletePlanForSamePublishedVersion()
    {
        var runtime = new TeamLabRuntime
        {
            Status = TeamLabRuntimeStatus.Scheduled,
            PublishedVersion = 2,
            WorkerNodeId = Guid.NewGuid(),
            NetworkPrefix = "10.180.0.0/24",
            PublicUdpMapping = new TeamLabPublicUdpMapping { PublicUdpPort = 32000 }
        };

        Assert.True(TeamLabPlanService.CanReuseScheduledRuntime(runtime, 2));
        Assert.False(TeamLabPlanService.CanReuseScheduledRuntime(runtime, 3));
    }
}
