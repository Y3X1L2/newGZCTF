using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GZCTF.Controllers;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabInternalControllerTests
{
    [Fact]
    public void TeamLabUdpMapRoute_UsesInternalSyncContract()
    {
        var method = typeof(InternalController).GetMethod(nameof(InternalController.GetTeamLabUdpMap));

        Assert.NotNull(method);
        Assert.Contains(method.GetCustomAttributes<HttpGetAttribute>(),
            attr => attr.Template == "teamlab-udp-map");
        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void BuildTeamLabUdpMappings_ReturnsOnlyOpenRunningMappingsWithSyncState()
    {
        var workerNodeId = Guid.Parse("02ec0080-77ef-4030-b075-4bce445ea2f3");
        var mappings = new List<TeamLabPublicUdpMapping>
        {
            new()
            {
                RuntimeId = 7,
                PublicUdpPort = 32007,
                WorkerTunnelIp = "10.24.0.27",
                WorkerWireGuardPort = 42007,
                RuleVersion = 3,
                IsSynced = true,
                Runtime = new TeamLabRuntime
                {
                    Id = 7,
                    GameId = 32,
                    TeamId = 33,
                    WorkerNodeId = workerNodeId,
                    Status = TeamLabRuntimeStatus.Running,
                    IsOpenToPlayers = true
                }
            },
            new()
            {
                RuntimeId = 8,
                PublicUdpPort = 32008,
                WorkerTunnelIp = "10.24.0.28",
                WorkerWireGuardPort = 42008,
                RuleVersion = 1,
                IsSynced = false,
                Runtime = new TeamLabRuntime
                {
                    Id = 8,
                    GameId = 32,
                    TeamId = 34,
                    WorkerNodeId = workerNodeId,
                    Status = TeamLabRuntimeStatus.Running,
                    IsOpenToPlayers = true
                }
            },
            new()
            {
                RuntimeId = 9,
                PublicUdpPort = 32009,
                WorkerTunnelIp = "10.24.0.29",
                WorkerWireGuardPort = 42009,
                RuleVersion = 1,
                IsSynced = true,
                Runtime = new TeamLabRuntime
                {
                    Id = 9,
                    GameId = 32,
                    TeamId = 35,
                    WorkerNodeId = workerNodeId,
                    Status = TeamLabRuntimeStatus.Scheduled,
                    IsOpenToPlayers = false
                }
            }
        }.AsQueryable();

        var result = InternalController.BuildTeamLabUdpMappings(mappings).ToArray();

        Assert.Equal([32007, 32008], result.Select(item => item.PublicUdpPort).ToArray());

        var item = result[0];
        Assert.Equal(32007, item.PublicUdpPort);
        Assert.Equal("10.24.0.27", item.WorkerTunnelIp);
        Assert.Equal(42007, item.WorkerWireGuardPort);
        Assert.Equal(7, item.RuntimeId);
        Assert.Equal(32, item.GameId);
        Assert.Equal(33, item.TeamId);
        Assert.Equal(workerNodeId, item.WorkerNodeId);
        Assert.Equal(3, item.RuleVersion);
        Assert.True(item.IsSynced);

        Assert.False(result[1].IsSynced);
    }
}
