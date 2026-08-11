using System;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Exercise.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Exercise;

public class ExerciseWorkflowRegressionTests
{
    [Fact]
    public async Task PublicManagement_RejectsCourseOwnedExercise()
    {
        await using var context = CreateContext();
        var courseOwned = new ExerciseChallenge
        {
            Title = "course",
            Content = "content",
            IsEnabled = true,
            TrainingCourseId = 42
        };
        context.ExerciseChallenges.Add(courseOwned);
        await context.SaveChangesAsync();

        var repository = new Mock<IExerciseChallengeRepository>(MockBehavior.Strict);
        var service = new ExerciseManagementService(
            context,
            repository.Object,
            new Mock<IBlobRepository>().Object);

        Assert.Null(await service.GetExerciseForUpdateAsync(courseOwned.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateExerciseAsync(new ExerciseChallenge
        {
            Id = courseOwned.Id,
            Title = "changed",
            Content = "changed"
        }));
        await service.RemoveExerciseAsync(courseOwned.Id);
        repository.VerifyNoOtherCalls();
        Assert.Equal("course", (await context.ExerciseChallenges.FindAsync(courseOwned.Id))!.Title);
    }

    [Fact]
    public async Task ImportFromGame_DeepCopiesExerciseAndFlagAttachments()
    {
        await using var context = CreateContext();
        var challenge = new GameChallenge
        {
            Title = "source",
            Content = "content",
            IsEnabled = true,
            Type = ChallengeType.StaticContainer,
            ContainerImage = "registry.example.test/ctf/source:latest",
            Attachment = new Attachment { Type = FileType.Remote, RemoteUrl = "https://example.test/exercise.zip" },
            Flags =
            [
                new FlagContext
                {
                    Flag = "flag{source}",
                    IsOccupied = true,
                    Attachment = new Attachment
                    {
                        Type = FileType.Remote,
                        RemoteUrl = "https://example.test/flag.zip"
                    }
                }
            ]
        };
        context.GameChallenges.Add(challenge);
        await context.SaveChangesAsync();
        var sourceAttachmentId = challenge.AttachmentId;
        var sourceFlagAttachmentId = challenge.Flags[0].AttachmentId;

        var blobRepository = new Mock<IBlobRepository>();
        var exerciseRepository = new ExerciseChallengeRepository(context, blobRepository.Object);
        var service = new ExerciseManagementService(context, exerciseRepository, blobRepository.Object);

        var imported = await service.ImportFromGameChallengeAsync(challenge.Id);

        Assert.Equal(ExercisePoolSource.Game, imported.PoolSource);
        Assert.Equal(Role.Teacher, imported.MinimumVisibleRole);
        Assert.NotEqual(sourceAttachmentId, imported.AttachmentId);
        Assert.Equal("https://example.test/exercise.zip", imported.Attachment?.RemoteUrl);
        var importedFlag = Assert.Single(imported.Flags);
        Assert.False(importedFlag.IsOccupied);
        Assert.NotEqual(sourceFlagAttachmentId, importedFlag.AttachmentId);
        Assert.Equal("https://example.test/flag.zip", importedFlag.Attachment?.RemoteUrl);

        context.GameChallenges.Add(new GameChallenge
        {
            GameId = challenge.GameId,
            Title = "attachment-only",
            Content = "content",
            Type = ChallengeType.StaticAttachment,
            IsEnabled = true,
            Flags = [new FlagContext { Flag = "flag{attachment}", IsOccupied = false }]
        });
        await context.SaveChangesAsync();

        var collected = await service.ImportFromGameAsync(challenge.GameId);
        Assert.Single(collected);
        Assert.Equal(imported.Id, collected[0].Id);
    }

    [Fact]
    public async Task ExerciseResourcePolicy_AllowsOnlyGlobalExerciseIdsForTeachers()
    {
        await using var context = CreateContext();
        var publicExercise = new ExerciseChallenge { Title = "public", Content = "content" };
        var courseExercise = new ExerciseChallenge { Title = "course", Content = "content", TrainingCourseId = 7 };
        context.ExerciseChallenges.AddRange(publicExercise, courseExercise);
        await context.SaveChangesAsync();
        var policy = new ExerciseApiTokenResourceGrantPolicy(context);

        Assert.True(await policy.CanGrantAsync(new ActorContext(Guid.NewGuid(), Role.Teacher), "*", default));
        Assert.True(await policy.CanGrantAsync(
            new ActorContext(Guid.NewGuid(), Role.Teacher), publicExercise.Id.ToString(), default));
        Assert.False(await policy.CanGrantAsync(
            new ActorContext(Guid.NewGuid(), Role.Teacher), courseExercise.Id.ToString(), default));
        Assert.False(await policy.CanGrantAsync(
            new ActorContext(Guid.NewGuid(), Role.Student), "*", default));
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }
}
