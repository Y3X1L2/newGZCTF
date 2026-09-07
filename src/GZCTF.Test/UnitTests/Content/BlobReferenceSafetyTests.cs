using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Repositories;
using GZCTF.Storage.Interface;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using TaskStatus = GZCTF.Utils.TaskStatus;

namespace GZCTF.Test.UnitTests.Content;

public class BlobReferenceSafetyTests
{
    [Theory]
    [InlineData("attachment")]
    [InlineData("course-resource")]
    [InlineData("course-video")]
    [InlineData("course-cover")]
    [InlineData("writeup")]
    [InlineData("poster")]
    [InlineData("team-avatar")]
    [InlineData("user-avatar")]
    public async Task ExplicitDelete_RejectsAllLiveConsumers(string consumer)
    {
        await using var context = CreateContext();
        var file = new LocalFile { Hash = new string('a', 64), Name = "shared.bin", ReferenceCount = 0 };
        context.Files.Add(file);
        await context.SaveChangesAsync();
        AddConsumer(context, file, consumer);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var storage = new Mock<IBlobStorage>(MockBehavior.Strict);
        var repository = new BlobRepository(context, NullLogger<BlobRepository>.Instance, storage.Object);

        Assert.Equal(TaskStatus.Denied, await repository.DeleteUnreferencedBlobByHash(file.Hash));
        Assert.True(await context.Files.AnyAsync());
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Release_UndercountedSharedAttachment_PreservesOtherConsumers()
    {
        await using var context = CreateContext();
        var file = new LocalFile { Hash = new string('b', 64), Name = "shared.bin", ReferenceCount = 1 };
        var first = new Attachment { LocalFile = file, Type = FileType.Local };
        var second = new Attachment { LocalFile = file, Type = FileType.Local };
        context.Attachments.AddRange(first, second);
        await context.SaveChangesAsync();
        var storage = new Mock<IBlobStorage>(MockBehavior.Strict);
        var repository = new BlobRepository(context, NullLogger<BlobRepository>.Instance, storage.Object);

        await repository.DeleteAttachment(first);
        await context.SaveChangesAsync();

        Assert.True(await context.Files.AnyAsync());
        Assert.Equal(file.Id, (await context.Attachments.SingleAsync()).LocalFileId);
        Assert.Equal(0u, file.ReferenceCount);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExplicitDelete_RejectsPendingReferenceWithoutSavingIt()
    {
        await using var context = CreateContext();
        var file = new LocalFile { Hash = new string('c', 64), Name = "pending.bin" };
        context.Files.Add(file);
        await context.SaveChangesAsync();
        context.TrainingCourseResources.Add(new TrainingCourseResource { LocalFile = file, Title = "pending" });
        var storage = new Mock<IBlobStorage>(MockBehavior.Strict);
        var repository = new BlobRepository(context, NullLogger<BlobRepository>.Instance, storage.Object);

        Assert.Equal(TaskStatus.Denied, await repository.DeleteUnreferencedBlobByHash(file.Hash));
        Assert.Empty(await context.TrainingCourseResources.ToArrayAsync());
        storage.VerifyNoOtherCalls();
    }

    static void AddConsumer(AppDbContext context, LocalFile file, string consumer)
    {
        switch (consumer)
        {
            case "attachment": context.Attachments.Add(new Attachment { LocalFile = file }); break;
            case "course-resource": context.TrainingCourseResources.Add(new TrainingCourseResource { LocalFile = file, Title = "resource" }); break;
            case "course-video": context.TrainingCourseChapters.Add(new TrainingCourseChapter { VideoFile = file, Title = "video" }); break;
            case "course-cover": context.TrainingCourses.Add(new TrainingCourse { Title = "course", Slug = "course", CoverFileHash = file.Hash }); break;
            case "writeup": context.Participations.Add(new Participation { Writeup = file }); break;
            case "poster": context.Games.Add(new Game { Title = "game", PosterHash = file.Hash }); break;
            case "team-avatar": context.Teams.Add(new Team { Name = "team", AvatarHash = file.Hash }); break;
            case "user-avatar": context.Users.Add(new UserInfo { UserName = "user", AvatarHash = file.Hash }); break;
            default: throw new ArgumentOutOfRangeException(nameof(consumer));
        }
    }

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
