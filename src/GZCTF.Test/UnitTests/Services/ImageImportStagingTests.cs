using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public sealed class ImageImportStagingTests
{
    [Fact]
    public async Task DeleteUnreferencedAsync_PreservesActiveFileAndDeletesOldOrphan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-staging-test-{Guid.NewGuid():N}");
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.ContentRootPath).Returns(root);
        var store = new FileImageImportStagingStore(
            environment.Object,
            Options.Create(new DockerRegistrySettings { MaxUploadSizeGb = 1 }));

        try
        {
            var active = await StageAsync(store, "active.tar", "active");
            var orphan = await StageAsync(store, "orphan.tar", "orphan");
            var recent = await StageAsync(store, "recent.tar", "recent");
            var old = DateTime.UtcNow.AddHours(-2);
            File.SetLastWriteTimeUtc(active.Path, old);
            File.SetLastWriteTimeUtc(orphan.Path, old);

            var removed = await store.DeleteUnreferencedAsync(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { active.Path },
                DateTimeOffset.UtcNow.AddHours(-1),
                CancellationToken.None);

            Assert.Equal(1, removed);
            Assert.True(File.Exists(active.Path));
            Assert.False(File.Exists(orphan.Path));
            Assert.True(File.Exists(recent.Path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ReconcileAsync_OnlyProtectsPendingAndRunningImports()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        var pending = CreateImport("pending.tar", ApiOperationStatus.Pending);
        var running = CreateImport("running.tar", ApiOperationStatus.Running);
        var succeeded = CreateImport("succeeded.tar", ApiOperationStatus.Succeeded);
        context.AddRange(
            pending.Operation,
            pending.Job,
            running.Operation,
            running.Job,
            succeeded.Operation,
            succeeded.Job);
        await context.SaveChangesAsync();

        IReadOnlySet<string>? activePaths = null;
        DateTimeOffset? cutoff = null;
        var staging = new Mock<IImageImportStagingStore>();
        staging.Setup(item => item.DeleteUnreferencedAsync(
                It.IsAny<IReadOnlySet<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlySet<string>, DateTimeOffset, CancellationToken>(
                (paths, olderThan, _) =>
                {
                    activePaths = paths;
                    cutoff = olderThan;
                })
            .ReturnsAsync(0);
        var now = new DateTimeOffset(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);
        var reconciler = new ImageImportStagingReconciler(context, staging.Object);

        await reconciler.ReconcileAsync(now, CancellationToken.None);

        Assert.NotNull(activePaths);
        Assert.Equal(2, activePaths.Count);
        Assert.Contains("pending.tar", activePaths);
        Assert.Contains("running.tar", activePaths);
        Assert.DoesNotContain("succeeded.tar", activePaths);
        Assert.Equal(now.AddHours(-1), cutoff);
    }

    [Fact]
    public async Task SubmitDockerArchiveAsync_AmbiguousPersistenceFailurePreservesStagedFileForReconcile()
    {
        var staged = new StagedImageImport(
            Path.Combine(Path.GetTempPath(), "staged.tar"),
            "archive.tar",
            7,
            new string('a', 64));
        var submissions = new Mock<IImageImportSubmissionStore>();
        submissions.Setup(item => item.SubmitAsync(
                It.IsAny<ImageImportSubmission>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var staging = new Mock<IImageImportStagingStore>();
        staging.Setup(item => item.StageAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(staged);
        var service = new ImageImportApplicationService(
            submissions.Object,
            Mock.Of<IImageImportExecutor>(),
            Mock.Of<IImageImportTemplateStore>(),
            staging.Object,
            new DockerImageReferencePolicy());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitDockerArchiveAsync(
            Guid.CreateVersion7(),
            new ActorContext(Guid.CreateVersion7(), Role.Admin),
            "idempotency-key",
            new MemoryStream(new byte[7]),
            "archive.tar",
            7,
            new DockerImageArchiveImportCommand("archive", null, OSType.Linux, null),
            CancellationToken.None));

        staging.Verify(
            item => item.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static async Task<GZCTF.Modules.Content.Application.StagedImageImport> StageAsync(
        FileImageImportStagingStore store,
        string fileName,
        string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await using var stream = new MemoryStream(bytes);
        return await store.StageAsync(
            stream,
            fileName,
            bytes.LongLength,
            null,
            CancellationToken.None);
    }

    private static (ApiOperation Operation, ImageImportJob Job) CreateImport(
        string stagedPath,
        ApiOperationStatus status)
    {
        var operation = new ApiOperation
        {
            Kind = ImageImportApplicationService.OperationKind,
            Status = status,
            ApiTokenId = Guid.CreateVersion7(),
            RouteKey = Guid.NewGuid().ToString("N"),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = Guid.NewGuid().ToString("N")
        };
        return (operation, new ImageImportJob
        {
            OperationId = operation.Id,
            SourceKind = ImageImportSourceKind.DockerArchive,
            StagedPath = stagedPath,
            RequestedName = stagedPath,
            RequestedTemplateKind = ImageType.Docker,
            RequestedOsType = OSType.Linux
        });
    }
}
