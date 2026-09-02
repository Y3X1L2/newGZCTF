using System;
using System.Text.Json;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Exercise;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Exercise.Domain;
using GZCTF.Modules.Exercise.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    public async Task PublicManagement_CanLoadDisabledDraftForEditing()
    {
        await using var context = CreateContext();
        var draft = new ExerciseChallenge
        {
            Title = "draft",
            Content = "content",
            IsEnabled = false
        };
        context.ExerciseChallenges.Add(draft);
        await context.SaveChangesAsync();

        var service = new ExerciseManagementService(
            context,
            new Mock<IExerciseChallengeRepository>(MockBehavior.Strict).Object,
            new Mock<IBlobRepository>().Object);

        Assert.NotNull(await service.GetExerciseForUpdateAsync(draft.Id));
    }

    [Fact]
    public async Task PublicManagement_UpdatePreservesCreatorAndReturnsCreatorName()
    {
        await using var context = CreateContext();
        var creator = new UserInfo { Id = Guid.NewGuid(), UserName = "creator" };
        var exercise = new ExerciseChallenge
        {
            Title = "original",
            Content = "content",
            CreatedById = creator.Id
        };
        context.Users.Add(creator);
        context.ExerciseChallenges.Add(exercise);
        await context.SaveChangesAsync();

        var service = new ExerciseManagementService(
            context,
            new Mock<IExerciseChallengeRepository>(MockBehavior.Strict).Object,
            new Mock<IBlobRepository>().Object);

        await service.UpdateExerciseAsync(new ExerciseChallenge
        {
            Id = exercise.Id,
            Title = "updated",
            Content = "updated"
        });
        context.ChangeTracker.Clear();

        var loaded = Assert.IsType<ExerciseChallenge>(await service.GetExerciseForUpdateAsync(exercise.Id));
        Assert.Equal(creator.Id, loaded.CreatedById);
        Assert.Equal("creator", loaded.CreatedBy?.UserName);
        Assert.Equal("creator", ExerciseManagementModel.FromExercise(loaded).CreatorUserName);
    }

    [Fact]
    public async Task PublicManagement_PageIncludesDraftsAndAdvancesById()
    {
        await using var context = CreateContext();
        context.ExerciseChallenges.AddRange(
            new ExerciseChallenge { Title = "first", Content = "content", IsEnabled = true },
            new ExerciseChallenge { Title = "draft", Content = "content", IsEnabled = false },
            new ExerciseChallenge
            {
                Title = "course-owned",
                Content = "content",
                IsEnabled = true,
                TrainingCourseId = 42
            });
        await context.SaveChangesAsync();

        var service = new ExerciseManagementService(
            context,
            new Mock<IExerciseChallengeRepository>(MockBehavior.Strict).Object,
            new Mock<IBlobRepository>().Object);

        var firstPage = await service.GetExercisePageAsync(null, 1, null);
        var first = Assert.Single(firstPage.Items);
        Assert.True(firstPage.HasMore);
        Assert.Equal("first", first.Title);

        var secondPage = await service.GetExercisePageAsync(null, 1, first.Id);
        var second = Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasMore);
        Assert.Equal("draft", second.Title);
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
    public async Task ImportFromTraining_CopiesCreatorAttribution()
    {
        await using var context = CreateContext();
        var creator = new UserInfo { Id = Guid.NewGuid(), UserName = "course-author" };
        var source = new ExerciseChallenge
        {
            Title = "course source",
            Content = "content",
            TrainingCourseId = 42,
            CreatedById = creator.Id,
            Type = ChallengeType.StaticAttachment,
            Attachment = new Attachment { Type = FileType.Remote, RemoteUrl = "https://example.test/source.zip" },
            Flags = [new FlagContext { Flag = "flag{training}", IsOccupied = false }]
        };
        context.Users.Add(creator);
        context.ExerciseChallenges.Add(source);
        await context.SaveChangesAsync();

        var blobRepository = new Mock<IBlobRepository>();
        var service = new ExerciseManagementService(
            context,
            new ExerciseChallengeRepository(context, blobRepository.Object),
            blobRepository.Object);

        var imported = Assert.Single(await service.ImportFromTrainingAsync(42, [source.Id]));

        Assert.Equal(creator.Id, imported.CreatedById);
        Assert.Equal(ExercisePoolSource.Training, imported.PoolSource);
    }

    [Fact]
    public async Task ExerciseList_ReturnsCreatorOnlyToTeachers()
    {
        await using var context = CreateContext();
        var creator = new UserInfo { Id = Guid.NewGuid(), UserName = "teacher-author" };
        context.Users.Add(creator);
        context.ExerciseChallenges.Add(new ExerciseChallenge
        {
            Title = "public",
            Content = "content",
            IsEnabled = true,
            CreatedById = creator.Id
        });
        await context.SaveChangesAsync();

        var service = new ExerciseService(
            context,
            new Mock<IExerciseInstanceRepository>().Object,
            new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance),
            new Mock<IOptionsSnapshot<ContainerPolicy>>().Object);

        var teacherModel = Assert.Single(await service.GetExerciseListAsync(role: Role.Teacher));
        var studentModel = Assert.Single(await service.GetExerciseListAsync(role: Role.Student));

        Assert.Equal("teacher-author", teacherModel.CreatorUserName);
        Assert.Null(studentModel.CreatorUserName);
    }

    [Fact]
    public async Task ExternalCreate_AssignsOperationActorAsCreator()
    {
        await using var context = CreateContext();
        var operationId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        context.ApiOperations.Add(new ApiOperation
        {
            Id = operationId,
            Kind = ExerciseExternalApplicationService.OperationKind,
            ActorUserId = actorUserId,
            RouteKey = "POST /api/open/v1/exercises",
            IdempotencyKey = "creator-test",
            RequestHash = "hash"
        });
        context.ExerciseMutationJobs.Add(new ExerciseMutationJob
        {
            OperationId = operationId,
            Kind = ExerciseMutationKind.Create,
            PayloadJson = JsonSerializer.Serialize(
                new ExerciseCreatePayload(new GZCTF.Modules.Exercise.Contracts.ExerciseCreateModel
                {
                    Title = "external",
                    Content = "content"
                }),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        await context.SaveChangesAsync();

        ExerciseChallenge? created = null;
        var managementService = new Mock<IExerciseManagementService>(MockBehavior.Strict);
        managementService
            .Setup(service => service.CreateExerciseAsync(
                It.IsAny<ExerciseChallenge>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExerciseChallenge exercise, CancellationToken _) =>
            {
                exercise.Id = 123;
                created = exercise;
                return exercise;
            });

        var handler = new ExerciseMutationOperationHandler(context, managementService.Object);
        await handler.ExecuteAsync(operationId, "test-worker", default);

        Assert.Equal(actorUserId, Assert.IsType<ExerciseChallenge>(created).CreatedById);
        managementService.VerifyAll();
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
