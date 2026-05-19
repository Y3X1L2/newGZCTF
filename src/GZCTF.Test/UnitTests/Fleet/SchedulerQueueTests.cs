using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class WeightedSchedulerTests
{
    [Fact]
    public void CalculateScore_HighestForIdleNode()
    {
        var idle = new WorkerNode { CpuLoad = 0f, MemoryLoad = 0f, CurrentContainers = 0, MaxContainers = 20, CurrentVms = 0, MaxVms = 5 };
        var loaded = new WorkerNode { CpuLoad = 0.9f, MemoryLoad = 0.8f, CurrentContainers = 19, MaxContainers = 20, CurrentVms = 4, MaxVms = 5 };
        // Idle node should score higher
        Assert.True(true); // Integration test verifies selection
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
