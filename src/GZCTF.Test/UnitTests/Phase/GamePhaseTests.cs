using Xunit;
using GZCTF.Services;

namespace GZCTF.Test.UnitTests.Phase;

public class GamePhaseTypeTests
{
    [Fact]
    public void PhaseRequiredType_Values_AreCorrect()
    {
        Assert.Equal(0, (int)PhaseRequiredType.CTF);
    }

    [Fact]
    public void PhaseCheckResult_Values_AreDistinct()
    {
        Assert.NotEqual((int)PhaseCheckResult.Allowed, (int)PhaseCheckResult.DisabledByPhase);
        Assert.NotEqual((int)PhaseCheckResult.Allowed, (int)PhaseCheckResult.NoActivePhase);
    }
}
