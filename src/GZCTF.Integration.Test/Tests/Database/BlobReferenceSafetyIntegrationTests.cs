using GZCTF.Integration.Test.Base;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Persistence.Governance;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Domain;
using GZCTF.Repositories;
using GZCTF.Storage;
using GZCTF.Storage.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = GZCTF.Utils.TaskStatus;

namespace GZCTF.Integration.Test.Tests.Database;

[Collection(nameof(IntegrationTestCollection))]
public sealed class BlobReferenceSafetyIntegrationTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task SharedReferenceAndRollback_KeepBytesUntilExplicitIdleDeletion()
    {
        await using var database = await IsolatedPostgresDatabase.CreateAsync(factory.DatabaseConnectionString);
        var root = Path.Combine(Path.GetTempPath(), "gzctf-blob-safety", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = StorageProviderFactory.Create($"disk://path={root}");
            await using var context = database.CreateContext();
            var repository = new BlobRepository(context, NullLogger<BlobRepository>.Instance, storage);
            await using var stream = new MemoryStream("shared-payload"u8.ToArray());
            var file = await repository.CreateOrUpdateBlobFromStream("shared.bin", stream);
            var path = StoragePath.Combine(PathHelper.Uploads, file.Location, file.Hash);
            var first = new Attachment { LocalFile = file, Type = FileType.Local };
            var second = new Attachment { LocalFile = file, Type = FileType.Local };
            context.Attachments.AddRange(first, second);
            await context.SaveChangesAsync();

            // Simulate legacy undercounted imports: two references, one counter.
            await repository.DeleteAttachment(first);
            await context.SaveChangesAsync();
            Assert.True(await storage.ExistsAsync(path));
            Assert.Equal(TaskStatus.Denied, await repository.DeleteUnreferencedBlobByHash(file.Hash));

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await repository.DeleteAttachment(second);
                await context.SaveChangesAsync();
                Assert.True(await storage.ExistsAsync(path));
                Assert.Equal(TaskStatus.Denied, await repository.DeleteUnreferencedBlobByHash(file.Hash));
                await transaction.RollbackAsync();
            }
            context.ChangeTracker.Clear();
            Assert.Single(await context.Attachments.ToArrayAsync());
            Assert.True(await storage.ExistsAsync(path));

            context.Attachments.RemoveRange(context.Attachments);
            await context.SaveChangesAsync();
            Assert.Equal(TaskStatus.Success, await repository.DeleteUnreferencedBlobByHash(file.Hash));
            Assert.False(await storage.ExistsAsync(path));
            Assert.Empty(await context.Files.ToArrayAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ConcurrentReferenceIncrements_DoNotLoseUpdates()
    {
        await using var database = await IsolatedPostgresDatabase.CreateAsync(factory.DatabaseConnectionString);
        var root = Path.Combine(Path.GetTempPath(), "gzctf-blob-safety", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = StorageProviderFactory.Create($"disk://path={root}");
            string hash;
            await using (var setup = database.CreateContext())
            {
                var repository = new BlobRepository(setup, NullLogger<BlobRepository>.Instance, storage);
                await using var stream = new MemoryStream("reference-counter"u8.ToArray());
                hash = (await repository.CreateOrUpdateBlobFromStream("counter.bin", stream)).Hash;
            }

            await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var context = database.CreateContext();
                var repository = new BlobRepository(context, NullLogger<BlobRepository>.Instance, storage);
                Assert.NotNull(await repository.IncrementBlobReference(hash));
            }));

            await using var verify = database.CreateContext();
            Assert.Equal(7u, (await verify.Files.SingleAsync()).ReferenceCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ConcurrentAssetUploadRetry_ReusesOneOperationAndOneBlobReference()
    {
        await using var database = await IsolatedPostgresDatabase.CreateAsync(factory.DatabaseConnectionString);
        var root = Path.Combine(Path.GetTempPath(), "gzctf-blob-safety", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = StorageProviderFactory.Create($"disk://path={root}");
            Guid actorId;
            Guid apiTokenId;
            await using (var setup = database.CreateContext())
            {
                var actor = new UserInfo { UserName = $"asset{Guid.NewGuid():N}"[..13] };
                var apiToken = new ApiToken
                {
                    Name = "concurrent asset upload",
                    CreatorId = actor.Id,
                    SecretHash = new byte[32]
                };
                setup.Users.Add(actor);
                setup.Set<ApiToken>().Add(apiToken);
                await setup.SaveChangesAsync();
                actorId = actor.Id;
                apiTokenId = apiToken.Id;
            }

            var payload = "concurrent-asset"u8.ToArray();
            var digest = $"sha-256=:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(payload))}:";
            async Task<AssetUploadResult> UploadAsync()
            {
                await using var context = database.CreateContext();
                var repository = new BlobRepository(context, NullLogger<BlobRepository>.Instance, storage);
                var service = new AssetApplicationService(context, repository,
                    new IdempotencyService(new EfApiOperationStore(context)));
                await using var content = new MemoryStream(payload);
                var file = new FormFile(content, 0, payload.Length, "file", "asset.bin");
                return await service.UploadAsync(file, null, apiTokenId, actorId,
                    "concurrent-upload", digest, default);
            }

            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => UploadAsync()));

            Assert.Single(results.Select(result => result.OperationId).Distinct());
            Assert.Single(results, result => !result.Reused);
            await using var verify = database.CreateContext();
            Assert.Equal(1u, (await verify.Files.SingleAsync()).ReferenceCount);
            Assert.Single(await verify.ApiOperations.ToArrayAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DeletionWaitsForHashBinding_ThenRejectsLiveCourseCover()
    {
        await using var database = await IsolatedPostgresDatabase.CreateAsync(factory.DatabaseConnectionString);
        var root = Path.Combine(Path.GetTempPath(), "gzctf-blob-safety", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = StorageProviderFactory.Create($"disk://path={root}");
            await using var writer = database.CreateContext();
            var repository = new BlobRepository(writer, NullLogger<BlobRepository>.Instance, storage);
            await using var content = new MemoryStream("cover-race"u8.ToArray());
            var file = await repository.CreateOrUpdateBlobFromStream("cover.bin", content);
            await using var deleter = database.CreateContext();
            var deleting = new BlobRepository(deleter, NullLogger<BlobRepository>.Instance, storage);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Task<TaskStatus> deletion;
            await using (var transaction = await writer.Database.BeginTransactionAsync())
            {
                await repository.IncrementBlobReference(file.Hash);
                deletion = deleting.DeleteUnreferencedBlobByHash(file.Hash, timeout.Token);
                writer.TrainingCourses.Add(new TrainingCourse
                {
                    Title = "Concurrent cover", Slug = "concurrent-cover", CoverFileHash = file.Hash
                });
                await writer.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            Assert.Equal(TaskStatus.Denied, await deletion);
            Assert.True(await storage.ExistsAsync(StoragePath.Combine(PathHelper.Uploads, file.Location, file.Hash)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Retention_PreservesLiveAssetAuthorizationButCleansUnrelatedHistory()
    {
        await using var database = await IsolatedPostgresDatabase.CreateAsync(factory.DatabaseConnectionString);
        await using var context = database.CreateContext();
        var hash = new string('d', 64);
        context.Files.Add(new LocalFile { Hash = hash, Name = "retained.bin" });
        var completedAt = DateTimeOffset.UtcNow.AddDays(-120);
        var live = new ApiOperation
        {
            Kind = "asset.upload", ResourceType = "asset", ResourceId = hash,
            Status = ApiOperationStatus.Succeeded, CompletedAt = completedAt
        };
        var gone = new ApiOperation
        {
            Kind = "asset.upload", ResourceType = "asset", ResourceId = new string('e', 64),
            Status = ApiOperationStatus.Succeeded, CompletedAt = completedAt
        };
        var unrelated = new ApiOperation
        {
            Kind = "exercise.mutation.v1", Status = ApiOperationStatus.Succeeded, CompletedAt = completedAt
        };
        context.ApiOperations.AddRange(live, gone, unrelated);
        await context.SaveChangesAsync();

        Assert.Equal(2, await new TerminalHistoryCleaner(context)
            .CleanApiOperationsAsync(DateTimeOffset.UtcNow.AddDays(-30), 20, default));
        Assert.Equal(live.Id, (await context.ApiOperations.AsNoTracking().SingleAsync()).Id);
    }
}
