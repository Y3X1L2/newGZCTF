using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public class AssetApplicationServiceTests
{
    [Fact]
    public async Task Upload_RecordsActorAndReturnsCreatorName()
    {
        await using var context = CreateContext();
        var actor = new UserInfo { Id = Guid.CreateVersion7(), UserName = "author" };
        context.Users.Add(actor);
        await context.SaveChangesAsync();

        var asset = new LocalFile
        {
            Id = 7,
            Hash = new string('a', 64),
            Name = "challenge.zip",
            FileSize = 4,
            CreatedById = actor.Id
        };
        var blobs = new Mock<IBlobRepository>(MockBehavior.Strict);
        blobs.Setup(repository => repository.CreateOrUpdateBlob(
                It.IsAny<IFormFile>(), "challenge.zip", It.IsAny<CancellationToken>(), actor.Id))
            .ReturnsAsync(asset);
        var service = new AssetApplicationService(context, blobs.Object);
        var file = new FormFile(new MemoryStream([1, 2, 3, 4]), 0, 4, "file", "challenge.zip");

        var result = await service.UploadAsync(file, "challenge.zip", actor.Id, CancellationToken.None);

        Assert.Equal(actor.UserName, result.CreatorUserName);
        Assert.Equal(asset.Hash, result.Hash);
        blobs.VerifyAll();
    }

    [Fact]
    public async Task Find_ReturnsOriginalUploaderWithoutMutatingOwnership()
    {
        await using var context = CreateContext();
        var actor = new UserInfo { Id = Guid.CreateVersion7(), UserName = "original-author" };
        context.Users.Add(actor);
        await context.SaveChangesAsync();

        var asset = new LocalFile
        {
            Id = 8,
            Hash = new string('b', 64),
            Name = "shared.zip",
            FileSize = 12,
            CreatedById = actor.Id
        };
        var blobs = new Mock<IBlobRepository>(MockBehavior.Strict);
        blobs.Setup(repository => repository.GetBlobByHash(asset.Hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);
        var service = new AssetApplicationService(context, blobs.Object);

        var result = await service.FindAsync(asset.Hash, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("original-author", result.CreatorUserName);
        Assert.Equal(asset.Hash, result.Hash);
        blobs.VerifyAll();
    }

    [Fact]
    public async Task Delete_RejectsAssetReferencedByAttachment()
    {
        await using var context = CreateContext();
        var asset = new LocalFile { Id = 9, Hash = new string('c', 64), Name = "in-use.zip" };
        context.Files.Add(asset);
        context.Attachments.Add(new Attachment { Type = FileType.Local, LocalFileId = asset.Id });
        await context.SaveChangesAsync();

        var blobs = new Mock<IBlobRepository>(MockBehavior.Strict);
        blobs.Setup(repository => repository.GetBlobByHash(asset.Hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);
        var service = new AssetApplicationService(context, blobs.Object);

        var result = await service.DeleteAsync(asset.Hash, CancellationToken.None);

        Assert.Equal(AssetDeleteStatus.InUse, result);
        blobs.VerifyAll();
    }

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
