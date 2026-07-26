using System.Collections.Generic;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class GameChallengeDetailModelTests
{
    [Theory]
    [InlineData(ChallengeType.DynamicContainer)]
    [InlineData(ChallengeType.DynamicAttachment)]
    public void FromInstance_HidesPerInstanceDynamicFlags(ChallengeType challengeType)
    {
        var challenge = CreateChallenge(challengeType,
        [
            new FlagContext { Id = 11, OrderIndex = 0 },
            new FlagContext { Id = 12, OrderIndex = 0 }
        ]);
        var instance = new GameInstance
        {
            ChallengeId = challenge.Id,
            Challenge = challenge
        };

        var model = ChallengeDetailModel.FromInstance(instance, 0);

        Assert.Null(model.Flags);
    }

    [Fact]
    public void FromInstance_PreservesConfiguredStaticMultiFlagSteps()
    {
        var challenge = CreateChallenge(ChallengeType.StaticContainer,
        [
            new FlagContext { Id = 21, OrderIndex = 2, Description = "Second step" },
            new FlagContext { Id = 20, OrderIndex = 1, Description = "First step" }
        ]);
        var instance = new GameInstance
        {
            ChallengeId = challenge.Id,
            Challenge = challenge
        };

        var model = ChallengeDetailModel.FromInstance(instance, 0);

        Assert.Collection(
            model.Flags!,
            first =>
            {
                Assert.Equal(20, first.Id);
                Assert.Equal(1, first.OrderIndex);
                Assert.Equal("First step", first.Description);
            },
            second =>
            {
                Assert.Equal(21, second.Id);
                Assert.Equal(2, second.OrderIndex);
                Assert.Equal("Second step", second.Description);
            });
    }

    private static GameChallenge CreateChallenge(ChallengeType type, List<FlagContext> flags)
    {
        var challenge = new GameChallenge
        {
            Id = 7,
            Title = "Game challenge",
            Content = "Challenge content",
            Type = type,
            Environment = EnvironmentType.Docker,
            Flags = flags
        };

        foreach (var flag in flags)
            flag.Challenge = challenge;

        return challenge;
    }
}
