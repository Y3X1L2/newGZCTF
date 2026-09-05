using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class AssetApplicationServiceTests
{
    static readonly byte[] Content = [1, 2, 3, 4];
    static string Hash => Convert.ToHexStringLower(SHA256.HashData(Content));
    static string Digest => $"sha-256=:{Convert.ToBase64String(SHA256.HashData(Content))}:";

    [Fact]
    public async Task Upload_RetryReusesCompletedOperationWithoutAddingReferences()
    {
        await using var context = CreateContext();
        var (service, blobs) = CreateService(context);
        var actor = Guid.NewGuid();
        var token = Guid.NewGuid();

        var first = await service.UploadAsync(File(), null, token, actor, "upload-1", Digest, default);
        var retry = await service.UploadAsync(File(), null, token, actor, "upload-1", Digest, default);

        Assert.Equal(first.OperationId, retry.OperationId);
        Assert.False(first.Reused);
        Assert.True(retry.Reused);
        Assert.Equal(Hash, retry.Asset.Hash);
        Assert.Equal(1u, (await context.Files.SingleAsync()).ReferenceCount);
        var operation = await context.ApiOperations.SingleAsync();
        Assert.Equal(ApiOperationStatus.Succeeded, operation.Status);
        Assert.Equal(AssetApplicationService.UploadOperationKind, operation.Kind);
        Assert.Equal("asset", operation.ResourceType);
        Assert.Equal(Hash, operation.ResourceId);
        Assert.Equal(actor, operation.ActorUserId);
        Assert.Equal(token, operation.ApiTokenId);
        Assert.True(await service.CanAccessAsync(actor, Hash, default));
        blobs.Verify(repository => repository.CreateOrUpdateBlobFromStream(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_ConflictingMetadataDoesNotWriteAgain()
    {
        await using var context = CreateContext();
        var (service, blobs) = CreateService(context);
        var actor = Guid.NewGuid();
        var token = Guid.NewGuid();
        await service.UploadAsync(File(), "original.zip", token, actor, "same-key", Digest, default);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            service.UploadAsync(File(), "changed.zip", token, actor, "same-key", Digest, default));

        Assert.Equal("original.zip", (await context.Files.SingleAsync()).Name);
        Assert.Single(context.ApiOperations);
        blobs.Verify(repository => repository.CreateOrUpdateBlobFromStream(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_ExistingContentPreservesMetadataAndRecordsTheVerifiedUploader()
    {
        await using var context = CreateContext();
        context.Files.Add(new LocalFile { Hash = Hash, Name = "original.zip", FileSize = Content.Length, ReferenceCount = 3 });
        await context.SaveChangesAsync();
        var (service, blobs) = CreateService(context);
        var actor = Guid.NewGuid();

        var result = await service.UploadAsync(File(), "renamed.zip", Guid.NewGuid(), actor, "new-key", Digest, default);

        Assert.Equal("original.zip", result.Asset.Name);
        Assert.Equal(3u, (await context.Files.SingleAsync()).ReferenceCount);
        Assert.True(await service.CanAccessAsync(actor, Hash, default));
        blobs.Verify(repository => repository.CreateOrUpdateBlobFromStream(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha-256=:invalid:")]
    [InlineData("sha-256=:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=:")]
    public async Task Upload_InvalidDigestDoesNotCreateOperationsOrWriteFiles(string digest)
    {
        await using var context = CreateContext();
        var (service, blobs) = CreateService(context);

        var error = await Assert.ThrowsAsync<AssetApiContractException>(() =>
            service.UploadAsync(File(), null, Guid.NewGuid(), Guid.NewGuid(), "upload", digest, default));

        Assert.StartsWith("asset_digest_", error.Code);
        Assert.Empty(context.ApiOperations);
        Assert.Empty(context.Files);
        blobs.Verify(repository => repository.CreateOrUpdateBlobFromStream(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("../attachment.zip")]
    [InlineData("folder\\attachment.zip")]
    [InlineData("bad\r\nname.zip")]
    public async Task Upload_RejectsInvalidDisplayNames(string name)
    {
        await using var context = CreateContext();
        var (service, _) = CreateService(context);
        var error = await Assert.ThrowsAsync<AssetApiContractException>(() =>
            service.UploadAsync(File(), name, Guid.NewGuid(), Guid.NewGuid(), "upload", Digest, default));
        Assert.Equal("asset_name_invalid", error.Code);
        Assert.Empty(context.ApiOperations);
    }

    [Fact]
    public async Task Upload_RetryDoesNotRecreateADeletedAsset()
    {
        await using var context = CreateContext();
        var (service, blobs) = CreateService(context);
        var token = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await service.UploadAsync(File(), null, token, actor, "upload", Digest, default);
        context.Files.Remove(await context.Files.SingleAsync());
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<AssetApiContractException>(() =>
            service.UploadAsync(File(), null, token, actor, "upload", Digest, default));

        Assert.Equal(410, error.StatusCode);
        Assert.Empty(context.Files);
        blobs.Verify(repository => repository.CreateOrUpdateBlobFromStream(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Find_RequiresSuccessfulUploadOwnershipOrExplicitGrant()
    {
        await using var context = CreateContext();
        var (service, _) = CreateService(context);
        var actor = Guid.NewGuid();
        await service.UploadAsync(File(), null, Guid.NewGuid(), actor, "upload", Digest, default);
        var unrelatedActor = Guid.NewGuid();

        Assert.NotNull(await service.FindAccessibleAsync(Hash, actor, false, default));
        Assert.Null(await service.FindAccessibleAsync(Hash, unrelatedActor, false, default));
        Assert.NotNull(await service.FindAccessibleAsync(Hash, unrelatedActor, true, default));
        Assert.Null(await service.FindAccessibleAsync("invalid", actor, true, default));
        var operation = await context.ApiOperations.SingleAsync();
        operation.Status = ApiOperationStatus.Failed;
        await context.SaveChangesAsync();
        Assert.False(await service.CanAccessAsync(actor, Hash, default));
    }

    [Fact]
    public async Task AssetGrantPolicy_RejectsOtherUploadsAndTeacherWildcards()
    {
        await using var context = CreateContext();
        var (service, _) = CreateService(context);
        var actor = Guid.NewGuid();
        await service.UploadAsync(File(), null, Guid.NewGuid(), actor, "upload", Digest, default);
        var policy = new AssetApiTokenResourceGrantPolicy(service);

        Assert.True(await policy.CanGrantAsync(new ActorContext(actor, Role.Teacher), Hash, default));
        Assert.False(await policy.CanGrantAsync(new ActorContext(Guid.NewGuid(), Role.Teacher), Hash, default));
        Assert.False(await policy.CanGrantAsync(new ActorContext(actor, Role.Teacher), "*", default));
        Assert.False(await policy.CanGrantAsync(new ActorContext(actor, Role.Student), Hash, default));
        Assert.True(await policy.CanGrantAsync(new ActorContext(actor, Role.Admin), "*", default));
        Assert.False(await policy.CanGrantAsync(new ActorContext(actor, Role.Admin), "invalid", default));
        Assert.DoesNotContain("assets:delete", ApiTokenScopes.All);
    }

    static IFormFile File() => new FormFile(new MemoryStream(Content), 0, Content.Length, "file", "attachment.zip");

    [Fact]
    public async Task Upload_FailureClearsTrackedChangesBeforeLaterMiddlewareSave()
    {
        await using var context = CreateContext();
        var (service, blobs) = CreateService(context);
        blobs.Setup(repository => repository.CreateOrUpdateBlobFromStream(
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((string name, Stream stream, CancellationToken token) =>
            {
                context.Files.Add(new LocalFile { Hash = Hash, Name = name });
                throw new IOException("simulated storage failure");
            });

        await Assert.ThrowsAsync<IOException>(() =>
            service.UploadAsync(File(), null, Guid.NewGuid(), Guid.NewGuid(), "failed-upload", Digest, default));

        Assert.Empty(context.ChangeTracker.Entries());
        await context.SaveChangesAsync();
        Assert.Empty(context.Files);
    }

    static (AssetApplicationService Service, Mock<IBlobRepository> Blobs) CreateService(AppDbContext context)
    {
        var blobs = new Mock<IBlobRepository>(MockBehavior.Strict);
        blobs.Setup(repository => repository.GetBlobByHash(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string? hash, CancellationToken token) => context.Files.SingleOrDefaultAsync(file => file.Hash == hash, token));
        blobs.Setup(repository => repository.CreateOrUpdateBlobFromStream(
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (string name, Stream stream, CancellationToken token) =>
            {
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
                var file = new LocalFile { Hash = hash, Name = name, FileSize = stream.Length };
                context.Files.Add(file);
                await context.SaveChangesAsync(token);
                return file;
            });
        return (new AssetApplicationService(context, blobs.Object,
            new IdempotencyService(new EfApiOperationStore(context))), blobs);
    }

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);
}
