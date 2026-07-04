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
    public void SelectNode_WhenTargetNodeIsProvided_OnlySelectsThatNode()
    {
        var penetrationNodeId = Guid.NewGuid();
        var otherHealthyNode = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.11"
        };
        var penetrationNode = new WorkerNode
        {
            Id = penetrationNodeId,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.10"
        };

        var result = TeamLabPlanService.SelectNode([otherHealthyNode, penetrationNode], penetrationNodeId);

        Assert.True(result.Success);
        Assert.Equal(penetrationNodeId, result.Node?.Id);
    }

    [Fact]
    public void SelectNode_WhenTargetNodeIsNotHealthy_DoesNotFallbackToOtherNode()
    {
        var penetrationNodeId = Guid.NewGuid();
        var otherHealthyNode = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.11"
        };
        var penetrationNode = new WorkerNode
        {
            Id = penetrationNodeId,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = false
        };

        var result = TeamLabPlanService.SelectNode([otherHealthyNode, penetrationNode], penetrationNodeId);

        Assert.False(result.Success);
        Assert.Null(result.Node);
        Assert.Contains("deployed penetration environment", result.Message);
    }

    [Fact]
    public void SelectNode_RejectsMalformedTeamLabTunnelIp()
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
            TeamLabTunnelIp = "not-an-ip"
        };

        var result = TeamLabPlanService.SelectNode([node]);

        Assert.False(result.Success);
        Assert.Null(result.Node);
        Assert.Contains("TeamLabNetwork", result.Message);
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

    [Theory]
    [InlineData(PenetrationDeploymentStatus.Published, 3, 3)]
    [InlineData(PenetrationDeploymentStatus.Running, 4, 4)]
    public void ResolvePublishedVersion_AcceptsPublishedOrRunningConfig(PenetrationDeploymentStatus status,
        int publishedVersion, int expected)
    {
        var result = TeamLabPlanService.ResolvePublishedVersion(new PenetrationConfig
        {
            Status = status,
            PublishedVersion = publishedVersion
        });

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(PenetrationDeploymentStatus.Draft, 1)]
    [InlineData(PenetrationDeploymentStatus.Published, 0)]
    [InlineData(PenetrationDeploymentStatus.Failed, 2)]
    public void ResolvePublishedVersion_ReturnsNullWhenNoDeployablePublishedSnapshot(
        PenetrationDeploymentStatus status, int publishedVersion)
    {
        var result = TeamLabPlanService.ResolvePublishedVersion(new PenetrationConfig
        {
            Status = status,
            PublishedVersion = publishedVersion
        });

        Assert.Null(result);
    }
}
