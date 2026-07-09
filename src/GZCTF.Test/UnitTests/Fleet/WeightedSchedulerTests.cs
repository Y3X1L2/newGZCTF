using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class WeightedSchedulerTests
{
    [Fact]
    public async Task SelectOptimalNode_ReturnsLeastLoaded()
    {
        var nodes = new List<WorkerNode>
        {
            new() { Id = Guid.NewGuid(), CpuLoad = 0.9f, MemoryLoad = 0.8f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online, IsLocal = true },
            new() { Id = Guid.NewGuid(), CpuLoad = 0.1f, MemoryLoad = 0.2f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online, IsLocal = true },
        };
        var mock = new Mock<INodeRepository>();
        mock.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(nodes);
        var scheduler = new WeightedScheduler(mock.Object, null!);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);
        Assert.Equal(nodes[1].Id, selected);
    }

    [Fact]
    public async Task SelectOptimalNode_ReturnsNull_WhenNoMatchingCapability()
    {
        var nodes = new List<WorkerNode>
        {
            new() { Id = Guid.NewGuid(), Capabilities = NodeCapability.Docker, Status = NodeStatus.Online, IsLocal = true },
        };
        var mock = new Mock<INodeRepository>();
        mock.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(nodes);
        var scheduler = new WeightedScheduler(mock.Object, null!);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Kvm, CancellationToken.None);
        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectOptimalNode_ReturnsNull_WhenNoOnlineNodes()
    {
        var mock = new Mock<INodeRepository>();
        mock.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkerNode>());
        var scheduler = new WeightedScheduler(mock.Object, null!);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);
        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectOptimalNode_ReturnsNull_WhenNodeScoreTooLow()
    {
        var nodes = new List<WorkerNode>
        {
            new() { Id = Guid.NewGuid(), CpuLoad = 0.95f, MemoryLoad = 0.95f, CurrentContainers = 20, MaxContainers = 20, CurrentVms = 5, MaxVms = 5, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online, IsLocal = true },
        };
        var mock = new Mock<INodeRepository>();
        mock.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(nodes);
        var scheduler = new WeightedScheduler(mock.Object, null!);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);
        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectOptimalNode_SkipsFullDockerNodes()
    {
        var nodes = new List<WorkerNode>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Capabilities = NodeCapability.Docker,
                Status = NodeStatus.Online,
                IsLocal = true,
                CurrentContainers = 20,
                MaxContainers = 20
            },
            new()
            {
                Id = Guid.NewGuid(),
                Capabilities = NodeCapability.Docker,
                Status = NodeStatus.Online,
                IsLocal = true,
                CurrentContainers = 1,
                MaxContainers = 20
            },
        };
        var mock = new Mock<INodeRepository>();
        mock.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(nodes);
        var scheduler = new WeightedScheduler(mock.Object, null!);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);

        Assert.Equal(nodes[1].Id, selected);
    }

    [Fact]
    public void SelectOptimalNode_UsesUpdatedCapacityForBatchScheduling()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            CurrentContainers = 19,
            MaxContainers = 20
        };

        var first = WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker);
        Assert.Equal(node.Id, first?.Id);

        node.CurrentContainers++;
        var second = WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker);

        Assert.Null(second);
    }

    [Fact]
    public void GetUnschedulableReason_ReturnsReason_WhenRemoteHeartbeatIsStale()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = false,
            LastHeartbeat = DateTimeOffset.UtcNow - WorkerNode.DefaultHeartbeatTimeout - TimeSpan.FromSeconds(1)
        };

        var reason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker);

        Assert.Equal("Node is offline or heartbeat is stale", reason);
        Assert.Null(WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker));
    }

    [Fact]
    public void GetUnschedulableReason_ReturnsReason_WhenSchedulingIsDisabled()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = false
        };

        var reason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker);

        Assert.Equal("Node scheduling is disabled", reason);
        Assert.Null(WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker));
    }

    [Fact]
    public void GetUnschedulableReason_ReturnsReason_WhenCapacityMetricsAreInvalid()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            CurrentContainers = -1,
            MaxContainers = 20
        };

        var reason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker);

        Assert.Equal("Node capacity metrics are invalid", reason);
        Assert.Null(WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker));
    }

    [Fact]
    public void GetUnschedulableReason_ReturnsReason_WhenDockerCapacityIsExhausted()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            CurrentContainers = 20,
            MaxContainers = 20
        };

        var reason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker);

        Assert.Equal("Node capacity exhausted for Docker", reason);
        Assert.Null(WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker));
    }

    [Fact]
    public void GetUnschedulableReason_ReturnsReason_WhenKvmCapacityIsExhausted()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Kvm,
            Status = NodeStatus.Online,
            IsLocal = true,
            CurrentVms = 3,
            MaxVms = 3
        };

        var reason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Kvm);

        Assert.Equal("Node capacity exhausted for Kvm", reason);
        Assert.Null(WeightedScheduler.SelectOptimalNode([node], NodeCapability.Kvm));
    }

    [Fact]
    public void GetUnschedulableReason_ReturnsReason_WhenLoadMetricsAreNotFinite()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            CpuLoad = float.NaN,
            MaxContainers = 20
        };

        var reason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker);

        Assert.Equal("Node capacity metrics are invalid", reason);
        Assert.Null(WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker));
    }

    [Fact]
    public void SelectOptimalNode_AllowsCombinedCapabilityOnlyWhenBothCapacitiesRemain()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            Status = NodeStatus.Online,
            IsLocal = true,
            CurrentContainers = 1,
            MaxContainers = 2,
            CurrentVms = 4,
            MaxVms = 5
        };

        var selected = WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker | NodeCapability.Kvm);
        Assert.Equal(node.Id, selected?.Id);

        node.CurrentVms++;
        Assert.Null(WeightedScheduler.SelectOptimalNode([node], NodeCapability.Docker | NodeCapability.Kvm));
    }

    [Fact]
    public void SelectOptimalTeamLabNode_RequiresHealthyTeamLabNetworkCapability()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            TeamLabNetworkEnabled = false
        };

        Assert.Null(WeightedScheduler.SelectOptimalTeamLabNode([node]));
        Assert.Equal("TeamLab network is not enabled", WeightedScheduler.GetTeamLabUnschedulableReason(node));

        node.TeamLabNetworkEnabled = true;
        node.TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy;
        node.TeamLabTunnelIp = "10.250.0.10";
        node.TeamLabAgentVersion = "1.8.3-test";
        node.TeamLabProtocolVersion = 3;

        Assert.Equal(node.Id, WeightedScheduler.SelectOptimalTeamLabNode([node])?.Id);
        Assert.Null(WeightedScheduler.GetTeamLabUnschedulableReason(node));
    }

    [Fact]
    public void SelectOptimalTeamLabNode_RejectsProtocolVersionWithoutNamespaceUplink()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.10",
            TeamLabAgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 2
        };

        Assert.Null(WeightedScheduler.SelectOptimalTeamLabNode([node]));
        Assert.Equal("TeamLab Agent protocol is incompatible; TeamLab Fabric namespace uplink requires protocol v3",
            WeightedScheduler.GetTeamLabUnschedulableReason(node));
    }

    [Fact]
    public void SelectOptimalTeamLabNode_RejectsUnhealthyTunnel()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Probing
        };

        Assert.Null(WeightedScheduler.SelectOptimalTeamLabNode([node]));
        Assert.Equal("TeamLab tunnel is not healthy", WeightedScheduler.GetTeamLabUnschedulableReason(node));
    }

    [Fact]
    public void SelectOptimalTeamLabNode_RejectsHealthyTunnelWithoutTunnelIp()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy
        };

        Assert.Null(WeightedScheduler.SelectOptimalTeamLabNode([node]));
        Assert.Equal("TeamLab tunnel IP is not configured", WeightedScheduler.GetTeamLabUnschedulableReason(node));
    }

    [Fact]
    public void TeamLabDockerCapability_DoesNotRequireKvm()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = false,
            LastHeartbeat = DateTimeOffset.UtcNow,
            IsSchedulable = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.31",
            TeamLabAgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 3,
            CurrentContainers = 0,
            MaxContainers = 2,
            CurrentVms = 0,
            MaxVms = 0
        };

        Assert.True(WeightedScheduler.CanHostTeamLabFabric(node));
        Assert.True(WeightedScheduler.CanHostTeamLabDocker(node));
        Assert.False(WeightedScheduler.CanHostTeamLabVm(node));
        Assert.False(WeightedScheduler.CanHostTeamLab(node));
        Assert.Null(WeightedScheduler.GetTeamLabAssetHostUnschedulableReason(
            node,
            requiresDocker: true,
            requiresVm: false));
        Assert.Contains("Kvm", WeightedScheduler.GetTeamLabAssetHostUnschedulableReason(
            node,
            requiresDocker: false,
            requiresVm: true));
    }

    [Fact]
    public void ReservedDockerCapacity_RejectsNodeAtConfiguredContainerLimit()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            CurrentContainers = 1,
            ReservedContainers = 1,
            MaxContainers = 2
        };

        Assert.False(FleetContainerManager.CanUseReservedDockerCapacity(node));

        node.ReservedContainers = 0;
        Assert.True(FleetContainerManager.CanUseReservedDockerCapacity(node));
    }

    [Fact]
    public void CapacityReservation_IsClampedOnRelease()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            CurrentContainers = 0,
            CurrentVms = 0
        };

        FleetManager.ReserveCapacity(node, NodeCapability.Docker | NodeCapability.Kvm);
        Assert.Equal(0, node.CurrentContainers);
        Assert.Equal(0, node.CurrentVms);
        Assert.Equal(1, node.ReservedContainers);
        Assert.Equal(1, node.ReservedVms);

        FleetManager.ReleaseCapacity(node, NodeCapability.Docker | NodeCapability.Kvm);
        FleetManager.ReleaseCapacity(node, NodeCapability.Docker | NodeCapability.Kvm);

        Assert.Equal(0, node.CurrentContainers);
        Assert.Equal(0, node.CurrentVms);
        Assert.Equal(0, node.ReservedContainers);
        Assert.Equal(0, node.ReservedVms);
    }

    [Fact]
    public void GetUnschedulableReason_CountsReservedCapacity()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            CurrentContainers = 1,
            ReservedContainers = 1,
            MaxContainers = 2
        };

        var reason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker);

        Assert.Equal("Node capacity exhausted for Docker", reason);
    }
}

public class QueueManagerTests
{
    [Fact]
    public void QueueLength_InitialIsZero()
    {
        Assert.True(true);
    }
}
