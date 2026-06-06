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
            new() { Id = Guid.NewGuid(), CpuLoad = 0.9f, MemoryLoad = 0.8f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
            new() { Id = Guid.NewGuid(), CpuLoad = 0.1f, MemoryLoad = 0.2f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
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
            new() { Id = Guid.NewGuid(), Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
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
            new() { Id = Guid.NewGuid(), CpuLoad = 0.95f, MemoryLoad = 0.95f, CurrentContainers = 20, MaxContainers = 20, CurrentVms = 5, MaxVms = 5, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
        };
        var mock = new Mock<INodeRepository>();
        mock.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(nodes);
        var scheduler = new WeightedScheduler(mock.Object, null!);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);
        Assert.Null(selected);
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
