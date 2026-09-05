using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Controllers;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Exercise;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Exercise.Api;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Modules.Exercise.Domain;
using GZCTF.Modules.Exercise.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using ExternalCreateModel = GZCTF.Modules.Exercise.Contracts.ExerciseCreateModel;

namespace GZCTF.Test.UnitTests.Exercise;

public class ExerciseImportAndAttachmentTests
{
    static readonly string Hash = new('a', 64);

    [Fact]
    public async Task ManagementList_IncludesDisabledPublicExercisesButStudentListDoesNot()
    {
        await using var context = CreateContext();
        context.ExerciseChallenges.AddRange(
            new ExerciseChallenge { Title = "enabled", Content = "content", IsEnabled = true },
            new ExerciseChallenge { Title = "disabled", Content = "content", IsEnabled = false },
            new ExerciseChallenge { Title = "teacher", Content = "content", IsEnabled = true, MinimumVisibleRole = Role.Teacher },
            new ExerciseChallenge { Title = "course", Content = "content", IsEnabled = true, TrainingCourseId = 42 });
        await context.SaveChangesAsync();
        var management = CreateService(context, new Mock<IBlobRepository>());
        var student = new ExerciseService(context, new Mock<IExerciseInstanceRepository>().Object, null!, null!);

        Assert.Equal(new[] { "enabled", "disabled", "teacher" },
            (await management.GetExerciseManagementListAsync()).Select(item => item.Title));
        Assert.Equal("enabled", Assert.Single(await student.GetExerciseListAsync(null)).Title);
        var action = typeof(ExerciseController).GetMethod(nameof(ExerciseController.GetExercisesForManagement))!;
        Assert.NotNull(action.GetCustomAttribute<RequireTeacherAttribute>());
    }

    [Fact]
    public async Task Create_LocalExerciseAndFlagAttachmentsAcquireSeparateReferences()
    {
        await using var context = CreateContext();
        var blob = new LocalFile { Hash = Hash, Name = "shared.zip" };
        context.Files.Add(blob);
        await context.SaveChangesAsync();
        var blobs = CreateBlobs(blob);
        var service = CreateService(context, blobs);
        var first = await service.CreateExerciseWithRelationsAsync(NewExercise(),
            [new ExerciseFlagCreateModel { Flag = "flag{first}", AttachmentType = FileType.Local, FileHash = Hash }],
            LocalAttachment());
        var second = await service.CreateExerciseWithRelationsAsync(NewExercise(), [], LocalAttachment());

        Assert.NotEqual(first.AttachmentId, second.AttachmentId);
        Assert.NotEqual(first.AttachmentId, Assert.Single(first.Flags).AttachmentId);
        blobs.Verify(repository => repository.IncrementBlobReference(Hash, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExternalUpdate_SameHashRetainsAttachmentAndFlagIds()
    {
        await using var context = CreateContext();
        var blob = new LocalFile { Hash = Hash, Name = "shared.zip" };
        var exercise = NewExercise();
        exercise.Attachment = new Attachment { Type = FileType.Local, LocalFile = blob };
        exercise.Flags = [new FlagContext
        {
            Flag = "flag{same}", OrderIndex = 1,
            Attachment = new Attachment { Type = FileType.Local, LocalFile = blob }
        }];
        context.ExerciseChallenges.Add(exercise);
        await context.SaveChangesAsync();
        var attachmentId = exercise.AttachmentId;
        var flagId = exercise.Flags[0].Id;
        var flagAttachmentId = exercise.Flags[0].AttachmentId;
        context.ChangeTracker.Clear();
        var blobs = CreateBlobs(blob);
        var service = CreateService(context, blobs);
        var loaded = (await service.GetExerciseForUpdateAsync(exercise.Id))!;

        await service.UpdateExerciseWithRelationsAsync(loaded,
            new List<ExerciseOpenApiFlagModel>
            {
                new() { Flag = "flag{same}", OrderIndex = 1, Attachment = new() { FileHash = Hash } }
            }, new ExerciseOpenApiAttachmentModel { FileHash = Hash });

        var updated = (await service.GetExerciseForUpdateAsync(exercise.Id))!;
        Assert.Equal(attachmentId, updated.AttachmentId);
        Assert.Equal(flagId, Assert.Single(updated.Flags).Id);
        Assert.Equal(flagAttachmentId, updated.Flags[0].AttachmentId);
        blobs.Verify(repository => repository.IncrementBlobReference(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        blobs.Verify(repository => repository.DeleteAttachment(It.IsAny<Attachment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_MissingReplacementDoesNotReleaseExistingAttachment()
    {
        await using var context = CreateContext();
        var exercise = NewExercise();
        exercise.Attachment = new Attachment { Type = FileType.Remote, RemoteUrl = "https://example.test/old.zip" };
        context.ExerciseChallenges.Add(exercise);
        await context.SaveChangesAsync();
        var oldId = exercise.AttachmentId;
        var blobs = new Mock<IBlobRepository>();
        var service = CreateService(context, blobs);

        await Assert.ThrowsAsync<ExerciseApiContractException>(() =>
            service.UpdateExerciseWithRelationsAsync(exercise, new List<ExerciseFlagCreateModel>(), LocalAttachment()));

        Assert.Equal(oldId, exercise.AttachmentId);
        blobs.Verify(repository => repository.DeleteAttachment(It.IsAny<Attachment>(), It.IsAny<CancellationToken>()), Times.Never);
        blobs.Verify(repository => repository.IncrementBlobReference(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ImageStatus.Importing, ImageType.Docker)]
    [InlineData(ImageStatus.Ready, ImageType.Qcow2)]
    public async Task Create_RejectsUnavailableOrIncompatibleTemplate(ImageStatus status, ImageType imageType)
    {
        await using var context = CreateContext();
        var template = new ImageTemplate { Name = "invalid", Status = status, ImageType = imageType, RegistryUrl = "registry.test/lab:v1" };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        var exercise = NewExercise();
        exercise.Type = ChallengeType.StaticContainer;
        exercise.ImageTemplateId = template.Id;
        exercise.ExposePort = 8080;

        await Assert.ThrowsAsync<ExerciseApiContractException>(() =>
            CreateService(context, new Mock<IBlobRepository>()).CreateExerciseWithRelationsAsync(exercise, [], null));

        Assert.Empty(await context.ExerciseChallenges.ToArrayAsync());
    }

    [Fact]
    public async Task ImportWorker_PreservesContainerFieldsAndResolvesTemplateRegistryReference()
    {
        await using var context = CreateContext();
        var template = new ImageTemplate { Name = "ready", Status = ImageStatus.Ready, ImageType = ImageType.Docker, RegistryUrl = "registry.test/lab:v1" };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        var item = ValidContainerImport();
        item.ImageTemplateId = template.Id;
        item.ContainerImage = null;
        var job = new ExerciseMutationJob
        {
            OperationId = Guid.NewGuid(), Kind = ExerciseMutationKind.Import,
            PayloadJson = JsonSerializer.Serialize(new ExerciseImportPayload([item]), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        context.ExerciseMutationJobs.Add(job);
        await context.SaveChangesAsync();
        var handler = new ExerciseMutationOperationHandler(context, CreateService(context, new Mock<IBlobRepository>()));

        await handler.ExecuteAsync(job.OperationId, "test-owner", default);

        var imported = Assert.Single(await context.ExerciseChallenges.ToArrayAsync());
        Assert.Equal(template.RegistryUrl, imported.ContainerImage);
        Assert.Equal(template.Id, imported.ImageTemplateId);
        Assert.Equal(EnvironmentType.Docker, imported.Environment);
        Assert.Equal(8080, imported.ExposePort);
        Assert.Equal(128, imported.MemoryLimit);
        Assert.Equal(512, imported.StorageLimit);
        Assert.Equal(2, imported.CPUCount);
        Assert.Equal(NetworkMode.Open, imported.NetworkMode);
        Assert.Null(job.PayloadJson);
        Assert.NotNull(job.ResultJson);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task CreateAndImport_ApplySamePortValidationBeforePersistingOperation(int port)
    {
        var store = new Mock<IExerciseMutationSubmissionStore>(MockBehavior.Strict);
        var service = new ExerciseExternalApplicationService(store.Object);
        var actor = new ActorContext(Guid.NewGuid(), Role.Teacher);
        var model = new ExternalCreateModel
        {
            Title = "container", Content = "content", Type = ChallengeType.StaticContainer,
            ContainerImage = "registry.test/lab:v1", ExposePort = port
        };
        var item = ValidContainerImport();
        item.ExposePort = port;

        var create = await Assert.ThrowsAsync<ExerciseApiContractException>(() =>
            service.SubmitCreateAsync(Guid.NewGuid(), actor, "create", model, "create", default));
        var import = await Assert.ThrowsAsync<ExerciseApiContractException>(() =>
            service.SubmitImportAsync(Guid.NewGuid(), actor, "import", [item], "import", default));

        Assert.Equal("exercise_port_invalid", create.Code);
        Assert.Equal(create.Code, import.Code);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_RejectsDuplicateExternalIdsBeforePersistingOperation()
    {
        var store = new Mock<IExerciseMutationSubmissionStore>(MockBehavior.Strict);
        var service = new ExerciseExternalApplicationService(store.Object);

        var error = await Assert.ThrowsAsync<ExerciseApiContractException>(() => service.SubmitImportAsync(
            Guid.NewGuid(), new ActorContext(Guid.NewGuid(), Role.Teacher), "import",
            [ValidContainerImport(), ValidContainerImport()], "import", default));

        Assert.Equal("exercise_external_id_invalid", error.Code);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_RejectsAmbiguousAttachmentBeforePersistingOperation()
    {
        var store = new Mock<IExerciseMutationSubmissionStore>(MockBehavior.Strict);
        var service = new ExerciseExternalApplicationService(store.Object);
        var model = new ExternalCreateModel
        {
            Title = "attachment", Content = "content",
            Attachment = new ExerciseOpenApiAttachmentModel { FileHash = Hash, RemoteUrl = "https://example.test/file.zip" }
        };

        await Assert.ThrowsAsync<ExerciseApiContractException>(() => service.SubmitCreateAsync(Guid.NewGuid(),
            new ActorContext(Guid.NewGuid(), Role.Teacher), "create", model, "create", default));

        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "other", false)]
    [InlineData(true, "matching", true)]
    [InlineData(false, null, false)]
    [InlineData(false, "matching", true)]
    public async Task ExternalBinding_RespectsAssetRestrictionBeforeUploadOwnership(
        bool ownsUpload, string? assetGrant, bool allowed)
    {
        await using var context = CreateContext();
        var actor = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        if (ownsUpload)
        {
            context.ApiOperations.Add(new ApiOperation
            {
                ActorUserId = actor, Kind = AssetApplicationService.UploadOperationKind,
                Status = ApiOperationStatus.Succeeded, ResourceType = "asset", ResourceId = Hash
            });
            await context.SaveChangesAsync();
        }
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actor.ToString()),
            new(ApiTokenClaimTypes.TokenId, tokenId.ToString()),
            new(ApiTokenClaimTypes.Resource, ApiTokenResourceClaim.Format("exercise", "*"))
        };
        if (assetGrant is not null)
            claims.Add(new Claim(ApiTokenClaimTypes.Resource,
                ApiTokenResourceClaim.Format("asset", assetGrant == "matching" ? Hash : new string('b', 64))));
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(),
                It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .Returns(async (ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            {
                var authContext = new AuthorizationHandlerContext(requirements, user, resource);
                await new ApiResourceAuthorizationHandler().HandleAsync(authContext);
                return authContext.HasSucceeded ? AuthorizationResult.Success() : AuthorizationResult.Failed();
            });
        var store = new Mock<IExerciseMutationSubmissionStore>(MockBehavior.Strict);
        if (allowed)
            store.Setup(service => service.SubmitAsync(It.IsAny<ExerciseMutationSubmission>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IdempotencyBeginResult(new ApiOperation(), false));
        var controller = new ExerciseOpenApiController(new Mock<IExerciseManagementService>().Object,
            new ExerciseExternalApplicationService(store.Object),
            new AssetApplicationService(context, new Mock<IBlobRepository>().Object, null!), authorization.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };
        var model = new ExternalCreateModel
        {
            Title = "attachment", Content = "content", Attachment = new ExerciseOpenApiAttachmentModel { FileHash = Hash }
        };

        if (allowed)
        {
            Assert.IsType<AcceptedResult>(await controller.Create(model, "create", default));
            store.Verify(service => service.SubmitAsync(It.IsAny<ExerciseMutationSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ExerciseApiContractException>(() => controller.Create(model, "create", default));
            Assert.Equal(403, error.StatusCode);
            store.VerifyNoOtherCalls();
        }
    }

    static ExerciseImportItemModel ValidContainerImport() => new()
    {
        ExternalId = "lab-1", Title = "container", Content = "content", Type = ChallengeType.StaticContainer,
        ContainerImage = "registry.test/lab:v1", ExposePort = 8080, MemoryLimit = 128,
        StorageLimit = 512, CPUCount = 2, NetworkMode = NetworkMode.Open
    };

    static ExerciseChallenge NewExercise() => new() { Title = "exercise", Content = "content", Type = ChallengeType.StaticAttachment };

    static AttachmentCreateModel LocalAttachment() => new() { AttachmentType = FileType.Local, FileHash = Hash };

    static Mock<IBlobRepository> CreateBlobs(LocalFile file)
    {
        var blobs = new Mock<IBlobRepository>();
        blobs.Setup(repository => repository.GetBlobByHash(file.Hash, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        blobs.Setup(repository => repository.IncrementBlobReference(file.Hash, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        return blobs;
    }

    static ExerciseManagementService CreateService(AppDbContext context, Mock<IBlobRepository> blobs) =>
        new(context, new ExerciseChallengeRepository(context, blobs.Object), blobs.Object);

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
}
