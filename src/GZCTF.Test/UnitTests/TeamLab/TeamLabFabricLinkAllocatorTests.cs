using System.Net;
using GZCTF.Modules.TeamLab.Application;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabFabricLinkAllocatorTests
{
    [Fact]
    public void FirstFree_SkipsEveryOverlappingLease()
    {
        var allocated = new[]
        {
            new IPNetwork(IPAddress.Parse("169.254.0.0"), 30),
            new IPNetwork(IPAddress.Parse("169.254.0.4"), 30),
            new IPNetwork(IPAddress.Parse("169.254.0.8"), 30)
        };

        var result = TeamLabFabricLinkAllocator.FirstFree("169.254.0.0/24", allocated);

        Assert.NotNull(result);
        Assert.Equal("169.254.0.12/30", result.Value.ToString());
    }

    [Fact]
    public void FirstFree_RejectsPoolSmallerThanLinkPrefix()
    {
        var exception = Assert.Throws<TeamLabApiContractException>(() =>
            TeamLabFabricLinkAllocator.FirstFree("169.254.0.0/31", []));

        Assert.Equal("fabric_link_pool_invalid", exception.Code);
    }
}
