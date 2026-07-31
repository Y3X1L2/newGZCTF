using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabAccessGrantTests
{
    [Fact]
    public void ResolveEntryNetwork_SelectsTheUniqueEntryNetworkOnAMultiNetworkShard()
    {
        var shard = new TeamLabRuntimeShard { Id = 17, Generation = 4 };
        var entry = new TeamLabRuntimeNetwork
        {
            Id = 101,
            Generation = 4,
            ShardId = shard.Id,
            IsEntry = true,
            TopologyKey = "entry"
        };
        var runtime = new TeamLabRuntime
        {
            Generation = 4,
            Networks =
            [
                new TeamLabRuntimeNetwork
                {
                    Id = 102,
                    Generation = 4,
                    ShardId = shard.Id,
                    IsEntry = false,
                    TopologyKey = "internal"
                },
                entry
            ]
        };

        var resolved = TeamLabAccessGrantService.ResolveEntryNetwork(runtime, shard);

        Assert.Same(entry, resolved);
    }

    [Fact]
    public void ResolveEntryNetwork_RejectsAnEntryNetworkAssignedToAnotherShard()
    {
        var shard = new TeamLabRuntimeShard { Id = 17, Generation = 4 };
        var runtime = new TeamLabRuntime
        {
            Generation = 4,
            Networks =
            [
                new TeamLabRuntimeNetwork
                {
                    Id = 101,
                    Generation = 4,
                    ShardId = 18,
                    IsEntry = true,
                    TopologyKey = "entry"
                }
            ]
        };

        Assert.Throws<TeamLabApiContractException>(() =>
            TeamLabAccessGrantService.ResolveEntryNetwork(runtime, shard));
    }
}
