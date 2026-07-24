using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Training.Application;
using GZCTF.Modules.Training.Domain;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Integration.Test.Tests.Database;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ImageTemplateOwnershipPersistenceTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task MaterializeImageTemplate_IsReadyWhenRegistryArtifactIsCommitted()
    {
        var ownerId = Guid.CreateVersion7();
        var operation = new ApiOperation
        {
            Kind = "image-import",
            Status = ApiOperationStatus.Running,
            ApiTokenId = Guid.CreateVersion7(),
            RouteKey = Guid.NewGuid().ToString("N"),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = Guid.NewGuid().ToString("N")
        };
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Users.Add(new UserInfo
        {
            Id = ownerId,
            UserName = $"import-{suffix}",
            NormalizedUserName = $"IMPORT-{suffix.ToUpperInvariant()}",
            Email = $"import-{suffix}@example.test",
            NormalizedEmail = $"IMPORT-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Teacher,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        });
        context.ApiTokens.Add(new ApiTokenEntity
        {
            Id = operation.ApiTokenId,
            Name = "image materialization test",
            CreatorId = ownerId,
            SecretHash = new byte[32],
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        context.ApiOperations.Add(operation);
        var job = new ImageImportJob
        {
            OperationId = operation.Id,
            SourceKind = ImageImportSourceKind.DockerReference,
            SourceReference = $"registry.example.test/training/{suffix}:latest",
            RequestedTemplateKind = ImageType.Docker,
            RequestedOsType = OSType.Linux,
            RequestedName = $"materialize-{suffix}",
            CreatedById = ownerId
        };
        context.ImageImportJobs.Add(job);
        await context.SaveChangesAsync();

        var store = new EfImageImportTemplateStore(context);
        var descriptor = await store.MaterializeAsync(
            job,
            new ImageImportArtifact(
                $"10.24.0.28:5000/ctf/{suffix}:latest",
                new string('a', 64),
                128,
                "test image"),
            true,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var template = await context.ImageTemplates.SingleAsync(item => item.Id == descriptor.Id);
        Assert.Equal(ImageStatus.Ready, template.Status);
        Assert.Null(template.ErrorMessage);

        await context.ImageTemplates.Where(item => item.Id == descriptor.Id).ExecuteDeleteAsync();
        await context.ApiOperations.Where(item => item.Id == operation.Id).ExecuteDeleteAsync();
        await context.ApiTokens.Where(item => item.Id == operation.ApiTokenId).ExecuteDeleteAsync();
        await context.Users.Where(item => item.Id == ownerId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DeleteCourse_RemovesBindingAndOwnedChallengeButPreservesTemplate()
    {
        var ownerId = Guid.CreateVersion7();
        int courseId;
        int templateId;
        int challengeId;

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var owner = new UserInfo
            {
                Id = ownerId,
                UserName = $"co-{suffix}",
                NormalizedUserName = $"CO-{suffix.ToUpperInvariant()}",
                Email = $"course-owner-{suffix}@example.test",
                NormalizedEmail = $"COURSE-OWNER-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
                EmailConfirmed = true,
                Role = Role.Teacher,
                RegisterTimeUtc = DateTimeOffset.UtcNow
            };
            var course = new TrainingCourse
            {
                Title = $"ownership-{suffix}",
                Slug = $"ownership-{suffix}",
                CreatedById = ownerId,
                UpdatedById = ownerId
            };
            var template = new ImageTemplate
            {
                Name = $"ownership-{suffix}",
                ImageType = ImageType.Docker,
                OSType = OSType.Linux,
                Status = ImageStatus.Ready,
                CreatedById = ownerId
            };
            context.AddRange(owner, course, template);
            await context.SaveChangesAsync();

            var challenge = new ExerciseChallenge
            {
                Title = $"snapshot-{suffix}",
                Content = "snapshot",
                TrainingCourseId = course.Id,
                ImageTemplateId = template.Id
            };
            context.AddRange(
                new TrainingCourseImageTemplateBinding
                {
                    CourseId = course.Id,
                    ImageTemplateId = template.Id,
                    AddedById = ownerId
                },
                challenge);
            await context.SaveChangesAsync();
            courseId = course.Id;
            templateId = template.Id;
            challengeId = challenge.Id;
        }

        await using (var deleteScope = factory.Services.CreateAsyncScope())
        {
            var service = deleteScope.ServiceProvider.GetRequiredService<TrainingCourseDeletionService>();
            var result = await service.DeleteAsync(
                courseId,
                new ActorContext(ownerId, Role.Teacher),
                CancellationToken.None);
            Assert.Equal(TrainingCourseDeletionStatus.Deleted, result.Status);
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verification = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verification.TrainingCourses.AnyAsync(item => item.Id == courseId));
        Assert.False(await verification.TrainingCourseImageTemplateBindings.AnyAsync(
            item => item.CourseId == courseId));
        Assert.False(await verification.ExerciseChallenges.AnyAsync(item => item.Id == challengeId));
        Assert.True(await verification.ImageTemplates.AnyAsync(item => item.Id == templateId));

        await verification.ImageTemplates.Where(item => item.Id == templateId).ExecuteDeleteAsync();
        await verification.Users.Where(item => item.Id == ownerId).ExecuteDeleteAsync();
    }
}
