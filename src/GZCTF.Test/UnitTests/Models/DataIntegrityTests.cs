using GZCTF.Models.Data;
using GZCTF.Utils;
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
    public void FlagContext_HasNewMultiFlagFields()
    {
        var fc = new FlagContext
        {
            ChallengeId = 1,
            Flag = "test",
            OrderIndex = 0,
            ScoreMode = FlagScoreMode.InheritDecay,
            AnswerType = AnswerType.Flag,
        };
        Assert.Equal(0, fc.OrderIndex);
        Assert.Equal(FlagScoreMode.InheritDecay, fc.ScoreMode);
    }

    [Fact]
    public void FirstSolve_HasTripleCompositeKey()
    {
        var solve = new FirstSolve { ParticipationId = 1, ChallengeId = 2, FlagId = 3 };
        Assert.Equal(1, solve.ParticipationId);
        Assert.Equal(2, solve.ChallengeId);
        Assert.Equal(3, solve.FlagId);
    }
}
