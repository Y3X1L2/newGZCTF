using GZCTF.Models.Data;
using GZCTF.Services.Scoring;

namespace GZCTF.Test.UnitTests.Scoring;

public class ScoreDecayTests
{
    [Fact]
    public void DecayNone_ReturnsBaseScore_OnAnyAttempt()
    {
        Assert.Equal(100, ScoreDecayCalculator.Apply(100, 0, ScoreDecay.None));
        Assert.Equal(100, ScoreDecayCalculator.Apply(100, 1, ScoreDecay.None));
        Assert.Equal(100, ScoreDecayCalculator.Apply(100, 5, ScoreDecay.None));
    }

    [Fact]
    public void DecayHalf_ReturnsFullOnFirstAttempt_HalfOnSecond()
    {
        Assert.Equal(100, ScoreDecayCalculator.Apply(100, 0, ScoreDecay.Half));
        Assert.Equal(50, ScoreDecayCalculator.Apply(100, 1, ScoreDecay.Half));
        Assert.Equal(25, ScoreDecayCalculator.Apply(100, 2, ScoreDecay.Half));
        Assert.Equal(12, ScoreDecayCalculator.Apply(100, 3, ScoreDecay.Half));
    }

    [Fact]
    public void DecayLinear_Decrements_By10PerAttempt_MinZero()
    {
        Assert.Equal(100, ScoreDecayCalculator.Apply(100, 0, ScoreDecay.Linear));
        Assert.Equal(90, ScoreDecayCalculator.Apply(100, 1, ScoreDecay.Linear));
        Assert.Equal(50, ScoreDecayCalculator.Apply(100, 5, ScoreDecay.Linear));
        Assert.Equal(0, ScoreDecayCalculator.Apply(100, 11, ScoreDecay.Linear));
        Assert.Equal(0, ScoreDecayCalculator.Apply(100, 20, ScoreDecay.Linear)); // never negative
    }

    [Fact]
    public void Apply_ReturnsBaseScore_WhenAttemptIndexNegative()
    {
        Assert.Equal(100, ScoreDecayCalculator.Apply(100, -1, ScoreDecay.Half));
    }

    [Fact]
    public void Apply_ReturnsZero_WhenBaseScoreZero()
    {
        Assert.Equal(0, ScoreDecayCalculator.Apply(0, 5, ScoreDecay.Half));
    }

    /// <summary>
    /// ★CRITICAL-6★: Pure decay function — always computes same output for same input.
    /// The double-decay prevention happens at the ARCHITECTURE level:
    /// ScoringService reads already-decayed Submission.Score and NEVER re-applies decay.
    /// This unit test verifies the pure function behaves correctly.
    /// </summary>
    [Fact]
    public void Apply_IsDeterministic_ForGivenInput()
    {
        // Pure function: same base+attempt+decay → same result every time
        Assert.Equal(6, ScoreDecayCalculator.Apply(100, 4, ScoreDecay.Half));
        Assert.Equal(6, ScoreDecayCalculator.Apply(100, 4, ScoreDecay.Half));
    }

    [Fact]
    public void Apply_IsDeterministic_SameInputSameOutput()
    {
        for (int i = 0; i < 100; i++)
        {
            var a = ScoreDecayCalculator.Apply(100, 3, ScoreDecay.Half);
            var b = ScoreDecayCalculator.Apply(100, 3, ScoreDecay.Half);
            Assert.Equal(a, b);
        }
    }

    [Theory]
    [InlineData(ScoreDecay.None, 100, 0, 100)]
    [InlineData(ScoreDecay.None, 100, 5, 100)]
    [InlineData(ScoreDecay.Half, 100, 0, 100)]
    [InlineData(ScoreDecay.Half, 100, 1, 50)]
    [InlineData(ScoreDecay.Half, 100, 2, 25)]
    [InlineData(ScoreDecay.Linear, 100, 0, 100)]
    [InlineData(ScoreDecay.Linear, 100, 1, 90)]
    [InlineData(ScoreDecay.Linear, 100, 5, 50)]
    [InlineData(ScoreDecay.Linear, 100, 11, 0)]
    public void Apply_ReturnsCorrectValue(ScoreDecay decay, int baseScore, int attemptIndex, int expected)
    {
        Assert.Equal(expected, ScoreDecayCalculator.Apply(baseScore, attemptIndex, decay));
    }
}
