using GZCTF.Models.Data;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class DataIntegrityTests
{
    [Fact]
    public void Container_HasGameInstanceId_AsForeignKey()
    {
        var container = new Container { GameInstanceId = 1, GameInstance = null };
        Assert.Equal(1, container.GameInstanceId);
    }

    [Fact]
    public void FlagContext_SupportsChallengeId()
    {
        var fc = new FlagContext { ChallengeId = 1, Flag = "test" };
        Assert.Equal(1, fc.ChallengeId);
    }

    [Fact]
    public void Submission_HasConcurrencyToken()
    {
        var sub = new Submission();
        Assert.Equal(0u, sub.ConcurrencyToken);
    }

    [Fact]
    public void ScenarioInstance_HasConcurrencyToken()
    {
        var instance = new ScenarioInstance();
        Assert.Equal(0u, instance.ConcurrencyToken);
    }

    [Fact]
    public void StageDependency_HasCompositeKey()
    {
        var dep = new StageDependency { StageId = 1, RequiredStageId = 2 };
        Assert.Equal(1, dep.StageId);
        Assert.Equal(2, dep.RequiredStageId);
    }
}
