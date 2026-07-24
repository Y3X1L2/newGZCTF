using GZCTF.Models.Data;
using GZCTF.Repositories;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Training;

public class ExerciseInstanceLegacyFlagTests
{
    [Fact]
    public void LoadedDynamicContainerWithoutRuntime_WithLegacyTestHash_ShouldRegenerate()
    {
        var instance = CreateInstance();

        Assert.True(ExerciseInstanceRepository.ShouldRegenerateLegacyDynamicFlag(instance));
    }

    [Fact]
    public void LoadedDynamicContainerWithRunningRuntime_WithLegacyTestHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.Container = new Container { Status = ContainerStatus.Running };

        Assert.False(ExerciseInstanceRepository.ShouldRegenerateLegacyDynamicFlag(instance));
    }

    [Fact]
    public void LoadedDynamicContainerWithoutRuntime_WithRandomHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.FlagContext!.Flag = "flag{0123456789ab}";

        Assert.False(ExerciseInstanceRepository.ShouldRegenerateLegacyDynamicFlag(instance));
    }

    [Fact]
    public void UnloadedDynamicContainerWithoutRuntime_WithLegacyTestHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.IsLoaded = false;

        Assert.False(ExerciseInstanceRepository.ShouldRegenerateLegacyDynamicFlag(instance));
    }

    [Fact]
    public void LoadedDynamicAttachmentWithoutRuntime_WithLegacyTestHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.Exercise.Type = ChallengeType.DynamicAttachment;

        Assert.False(ExerciseInstanceRepository.ShouldRegenerateLegacyDynamicFlag(instance));
    }

    [Fact]
    public void LoadedDynamicContainerWithoutTeamHashTemplate_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.Exercise.FlagTemplate = "flag{[GUID]}";

        Assert.False(ExerciseInstanceRepository.ShouldRegenerateLegacyDynamicFlag(instance));
    }

    private static ExerciseInstance CreateInstance() =>
        new()
        {
            IsLoaded = true,
            Exercise = new ExerciseChallenge
            {
                Type = ChallengeType.DynamicContainer,
                FlagTemplate = "flag{[TEAM_HASH]}"
            },
            FlagContext = new FlagContext { Flag = "flag{TestTeamHash}" }
        };
}
