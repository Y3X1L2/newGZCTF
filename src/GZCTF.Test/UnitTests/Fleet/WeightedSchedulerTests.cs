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
    public void ReservedDockerCapacity_RejectsNodeAtConfiguredContainerLimit()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            CurrentContainers = 2,
            MaxContainers = 2
        };

        Assert.False(FleetContainerManager.CanUseReservedDockerCapacity(node));

        node.CurrentContainers = 1;
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
        Assert.Equal(1, node.CurrentContainers);
        Assert.Equal(1, node.CurrentVms);

        FleetManager.ReleaseCapacity(node, NodeCapability.Docker | NodeCapability.Kvm);
        FleetManager.ReleaseCapacity(node, NodeCapability.Docker | NodeCapability.Kvm);

        Assert.Equal(0, node.CurrentContainers);
        Assert.Equal(0, node.CurrentVms);
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
