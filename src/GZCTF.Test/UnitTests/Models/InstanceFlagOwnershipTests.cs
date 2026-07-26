using GZCTF.Models.Data;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class InstanceFlagOwnershipTests
{
    [Fact]
    public void CreateInstanceFlag_DoesNotAttachFlagToGameOrExercise()
    {
        var flag = FlagContext.CreateInstanceFlag("flag{instance-owned}");

        Assert.Equal("flag{instance-owned}", flag.Flag);
        Assert.True(flag.IsOccupied);
        Assert.Null(flag.ChallengeId);
        Assert.Null(flag.Challenge);
        Assert.Null(flag.ExerciseId);
        Assert.Null(flag.Exercise);
    }
}
