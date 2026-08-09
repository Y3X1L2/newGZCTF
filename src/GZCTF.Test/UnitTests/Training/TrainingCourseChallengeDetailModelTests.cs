using System;
using System.Collections.Generic;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Training;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Training;

public class TrainingCourseChallengeDetailModelTests
{
    [Theory]
    [InlineData(ChallengeType.DynamicContainer)]
    [InlineData(ChallengeType.DynamicAttachment)]
    public void FromInstance_HidesPerInstanceDynamicFlags(ChallengeType challengeType)
    {
        var exercise = CreateExercise(challengeType,
        [
            new FlagContext { Id = 11, OrderIndex = 0 },
            new FlagContext { Id = 12, OrderIndex = 0 }
        ]);
        var instance = new ExerciseInstance
        {
            ExerciseId = exercise.Id,
            Exercise = exercise
        };

        var model = TrainingCourseChallengeDetailModel.FromInstance(3, 4, instance, 0, false);

        Assert.Null(model.Flags);
    }

    [Fact]
    public void FromInstance_PreservesConfiguredStaticMultiFlagSteps()
    {
        var exercise = CreateExercise(ChallengeType.StaticAttachment,
        [
            new FlagContext { Id = 21, OrderIndex = 2, Description = "Second step" },
            new FlagContext { Id = 20, OrderIndex = 1, Description = "First step" }
        ]);
        var instance = new ExerciseInstance
        {
            ExerciseId = exercise.Id,
            Exercise = exercise
        };

        var model = TrainingCourseChallengeDetailModel.FromInstance(3, 4, instance, 0, false);

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

    [Fact]
    public void FromInstance_HidesExpiredContainerRuntime()
    {
        var exercise = CreateExercise(ChallengeType.DynamicContainer, []);
        var instance = new ExerciseInstance
        {
            ExerciseId = exercise.Id,
            Exercise = exercise,
            Container = new Container
            {
                Status = ContainerStatus.Running,
                ExpectStopAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                EntryStatus = ContainerEntryStatus.Ready,
                PublicIP = "203.195.157.191",
                PublicPort = 30002
            }
        };

        var model = TrainingCourseChallengeDetailModel.FromInstance(3, 4, instance, 0, false);

        Assert.Null(model.Context.CloseTime);
        Assert.Null(model.Context.InstanceEntry);
    }

    [Fact]
    public void FromInstance_ExposesActiveContainerRuntime()
    {
        var exercise = CreateExercise(ChallengeType.DynamicContainer, []);
        var stopAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var instance = new ExerciseInstance
        {
            ExerciseId = exercise.Id,
            Exercise = exercise,
            Container = new Container
            {
                Status = ContainerStatus.Running,
                ExpectStopAt = stopAt,
                EntryStatus = ContainerEntryStatus.Ready,
                PublicIP = "203.195.157.191",
                PublicPort = 30002
            }
        };

        var model = TrainingCourseChallengeDetailModel.FromInstance(3, 4, instance, 0, false);

        Assert.Equal(stopAt, model.Context.CloseTime);
        Assert.Equal("203.195.157.191:30002", model.Context.InstanceEntry);
    }

    private static ExerciseChallenge CreateExercise(ChallengeType type, List<FlagContext> flags)
    {
        var exercise = new ExerciseChallenge
        {
            Id = 7,
            Title = "Training challenge",
            Content = "Challenge content",
            Type = type,
            Environment = EnvironmentType.Docker,
            Flags = flags
        };

        foreach (var flag in flags)
            flag.Exercise = exercise;

        return exercise;
    }
}
