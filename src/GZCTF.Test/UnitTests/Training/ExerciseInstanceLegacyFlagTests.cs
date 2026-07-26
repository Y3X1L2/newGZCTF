using System;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Training;

public class ExerciseInstanceLegacyFlagTests
{
    [Fact]
    public void LoadedDynamicContainerWithoutRuntime_WithLegacyTestHash_ShouldRegenerate()
    {
        var instance = CreateInstance();

        Assert.True(instance.TryRegenerateLegacyDynamicFlag());
        Assert.DoesNotContain("TestTeamHash", instance.FlagContext!.Flag, StringComparison.Ordinal);
        Assert.Null(instance.FlagContext.ExerciseId);
        Assert.Null(instance.FlagContext.Exercise);
    }

    [Fact]
    public void LoadedDynamicContainerWithRunningRuntime_WithLegacyTestHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.Container = new Container { Status = ContainerStatus.Running };

        Assert.False(instance.TryRegenerateLegacyDynamicFlag());
    }

    [Fact]
    public void LoadedDynamicContainerWithPendingRuntime_WithLegacyTestHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.Container = new Container { Status = ContainerStatus.Pending };

        Assert.False(instance.TryRegenerateLegacyDynamicFlag());
    }

    [Fact]
    public void LoadedDynamicContainerWithDestroyedRuntime_WithLegacyTestHash_ShouldNotRegenerateBeforeCleanup()
    {
        var instance = CreateInstance();
        instance.Container = new Container { Status = ContainerStatus.Destroyed };

        Assert.False(instance.TryRegenerateLegacyDynamicFlag());
    }

    [Fact]
    public void LoadedDynamicContainerWithoutRuntime_WithRandomHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.FlagContext!.Flag = "flag{0123456789ab}";

        Assert.False(instance.TryRegenerateLegacyDynamicFlag());
    }

    [Fact]
    public void UnloadedDynamicContainerWithoutRuntime_WithLegacyTestHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.IsLoaded = false;

        Assert.False(instance.TryRegenerateLegacyDynamicFlag());
    }

    [Fact]
    public void LoadedDynamicAttachmentWithoutRuntime_WithLegacyTestHash_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.Exercise.Type = ChallengeType.DynamicAttachment;

        Assert.False(instance.TryRegenerateLegacyDynamicFlag());
    }

    [Fact]
    public void LoadedDynamicContainerWithoutTeamHashTemplate_ShouldNotRegenerate()
    {
        var instance = CreateInstance();
        instance.Exercise.FlagTemplate = "flag{[GUID]}";

        Assert.False(instance.TryRegenerateLegacyDynamicFlag());
    }

    private static ExerciseInstance CreateInstance()
    {
        var exercise = new ExerciseChallenge
        {
            Id = 7,
            Type = ChallengeType.DynamicContainer,
            FlagTemplate = "flag{[TEAM_HASH]}"
        };
        return new ExerciseInstance
        {
            IsLoaded = true,
            ExerciseId = exercise.Id,
            Exercise = exercise,
            FlagContext = new FlagContext
            {
                Flag = "flag{TestTeamHash}",
                ExerciseId = exercise.Id,
                Exercise = exercise
            }
        };
    }
}
