using System;
using System.Collections.Generic;
using GZCTF.Models.Data;
using GZCTF.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabPlanServiceTests
{
    [Fact]
    public void SelectNode_RejectsWhenNoHealthyTeamLabNode()
    {
        var nodes = new[]
        {
            new WorkerNode
            {
                Status = NodeStatus.Online,
                IsLocal = true,
                IsSchedulable = true,
                Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
                TeamLabNetworkEnabled = false
            }
        };

        var result = TeamLabPlanService.SelectNode(nodes);

        Assert.False(result.Success);
        Assert.Contains("TeamLabNetwork", result.Message);
    }

    [Fact]
    public void SelectNode_ReturnsHealthyTeamLabNode()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.10"
        };

        var result = TeamLabPlanService.SelectNode([node]);

        Assert.True(result.Success);
        Assert.Equal(node.Id, result.Node?.Id);
    }

    [Fact]
    public void AllocatePublicUdpPort_UsesConfiguredRangeAndSkipsUsedPorts()
    {
        var port = TeamLabPlanService.AllocatePublicUdpPort(32000, 32003, new HashSet<int> { 32000, 32001 });

        Assert.Equal(32002, port);
    }

    [Fact]
    public void AllocatePublicUdpPort_ReturnsNullWhenRangeIsExhausted()
    {
        var port = TeamLabPlanService.AllocatePublicUdpPort(32000, 32001, new HashSet<int> { 32000, 32001 });

        Assert.Null(port);
    }
}
