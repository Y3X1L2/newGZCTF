using GZCTF.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabDeploymentServiceTests
{
    [Fact]
    public void DeploymentPlan_UsesTraceableLinuxResourceNames()
    {
        var names = TeamLabDeploymentService.BuildResourceNames(runtimeId: 123, networkKeys: ["dmz", "data"]);

        Assert.All(names.Bridges, name => Assert.True(name.Length <= 15));
        Assert.Contains(names.Bridges, name => name.StartsWith("tl123-"));
        Assert.True(names.RouterNamespace.Length <= 15);
        Assert.True(names.WireGuardInterface.Length <= 15);
    }
}
