using System;
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
        Assert.Empty(runtime.Shards);
    }

    [Fact]
    public void TeamLabRuntimeShard_DefaultsAreRecoverable()
    {
        var runtime = new TeamLabRuntime { GameId = 1, TeamId = 2 };
        var shard = new TeamLabRuntimeShard
        {
            Runtime = runtime,
            WorkerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };

        Assert.Equal(TeamLabRuntimeStatus.Pending, shard.Status);
        Assert.Equal(0, shard.RouteVersion);
        Assert.NotEqual(default, shard.CreatedAt);
        Assert.Null(shard.LastError);
    }

    [Fact]
    public void PublicUdpMapping_DefaultsAreUnsynced()
    {
        var mapping = new TeamLabPublicUdpMapping { PublicUdpPort = 32000 };

        Assert.False(mapping.IsSynced);
        Assert.Equal(0, mapping.RuleVersion);
    }

    [Fact]
    public void VmInstance_DefaultRdpCredentialMatchesWindowsTemplate()
    {
        var instance = new VmInstance();

        Assert.Equal("player", instance.RdpUsername);
        Assert.Equal("qwer1234!", instance.RdpPassword);
    }
}
