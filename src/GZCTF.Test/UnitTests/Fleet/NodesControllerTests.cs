using GZCTF.Controllers;
using GZCTF.Models.Data;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class NodesControllerTests
{
    [Fact]
    public void NodeRegisterRequest_Defaults()
    {
        var req = new NodeRegisterRequest();
        Assert.Equal(NodeCapability.Docker, req.Capabilities);
        Assert.Equal(20, req.MaxContainers);
        Assert.Equal(5, req.MaxVms);
    }
}
