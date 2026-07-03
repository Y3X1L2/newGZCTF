using GZCTF.Models.Data;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabModelTests
{
    [Fact]
    public void TeamLabRuntime_DefaultsAreSafe()
    {
        var runtime = new TeamLabRuntime { GameId = 1, TeamId = 2 };

        Assert.Equal(TeamLabRuntimeStatus.Pending, runtime.Status);
        Assert.Equal(string.Empty, runtime.NetworkPrefix);
        Assert.False(runtime.IsOpenToPlayers);
    }

    [Fact]
    public void PublicUdpMapping_DefaultsAreUnsynced()
    {
        var mapping = new TeamLabPublicUdpMapping { PublicUdpPort = 32000 };

        Assert.False(mapping.IsSynced);
        Assert.Equal(0, mapping.RuleVersion);
    }
}
