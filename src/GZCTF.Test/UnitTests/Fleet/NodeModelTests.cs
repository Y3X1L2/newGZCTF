using GZCTF.Models.Data;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class WorkerNodeTests
{
    [Fact]
    public void WorkerNode_DefaultsAreSetCorrectly()
    {
        var node = new WorkerNode();
        Assert.Equal(NodeCapability.Docker, node.Capabilities);
        Assert.Equal(NodeStatus.Unknown, node.Status);
        Assert.Equal(20, node.MaxContainers);
        Assert.Equal(5, node.MaxVms);
        Assert.Equal(28231, node.TotalPorts);
    }

    [Fact]
    public void NodeCapability_SupportsFlagCombination()
    {
        var combined = NodeCapability.Docker | NodeCapability.Kvm;
        Assert.True(combined.HasFlag(NodeCapability.Docker));
        Assert.True(combined.HasFlag(NodeCapability.Kvm));
    }
}

public class DeploymentTargetTests
{
    [Fact]
    public void DeploymentTarget_Defaults()
    {
        var target = new DeploymentTarget();
        Assert.Equal(TargetStatus.Pending, target.Status);
        Assert.Equal(TargetType.Docker, target.Type);
    }
}
