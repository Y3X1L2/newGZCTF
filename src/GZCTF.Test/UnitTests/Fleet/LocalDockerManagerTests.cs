using System;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Manager;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class LocalDockerManagerTests
{
    [Fact]
    public void ManagedLabels_MatchAgentInventoryContract()
    {
        var userId = Guid.NewGuid();
        var labels = DockerManager.BuildManagedLabels(new ContainerConfig
        {
            RuntimeId = 0,
            Generation = 3,
            TeamId = "1",
            UserId = userId,
            ChallengeId = 19
        });

        Assert.Equal("GZCTF", labels["ManagedBy"]);
        Assert.Equal("3", labels["GZCTF.Generation"]);
        Assert.Equal("0", labels["GZCTF.RuntimeId"]);
        Assert.Equal("1", labels["TeamId"]);
        Assert.Equal(userId.ToString(), labels["UserId"]);
        Assert.Equal("19", labels["ChallengeId"]);
    }
}
