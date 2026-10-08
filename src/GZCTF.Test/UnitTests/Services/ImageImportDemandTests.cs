using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public sealed class ImageImportDemandTests
{
    [Theory]
    [InlineData(ImageImportSourceKind.DockerArchive, ImageType.Docker)]
    [InlineData(ImageImportSourceKind.VmQcow2, ImageType.Qcow2)]
    public async Task CompletedImport_DeletesStagingAndCompletesWithoutWorkerCopies(
        ImageImportSourceKind source, ImageType kind)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var template = new ImageTemplate
        {
            Id = 42, Name = "artifact", ImageType = kind, Status = ImageStatus.Ready,
            RegistryUrl = "registry.invalid/ctf/artifact:release", ImageHash = new string('a', 64)
        };
        var job = new ImageImportJob
        {
            OperationId = Guid.NewGuid(), SourceKind = source,
            StagedPath = "task-owned-staging", ImageTemplateId = template.Id
        };
        context.AddRange(template, job, new WorkerNode
        {
            Name = "selected-later", Status = NodeStatus.Online, IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm
        }, new WorkerNode
        {
            Name = "unselected-low-space", Status = NodeStatus.Online, IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm
        });
        await context.SaveChangesAsync();
        var staging = new Mock<IImageImportStagingStore>(MockBehavior.Strict);
        staging.Setup(item => item.DeleteAsync(job.StagedPath, CancellationToken.None))
            .Returns(Task.CompletedTask);
        var store = new Mock<IApiOperationStore>(MockBehavior.Strict);
        store.Setup(item => item.UpdateProgressAsync(job.OperationId, "lease", "image-ready", 1, 1,
                "image-template", "42", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = new ImageImportOperationHandler(context, null!, new ApiOperationService(store.Object), staging.Object);

        await handler.ExecuteAsync(job.OperationId, "lease", CancellationToken.None);
        await handler.ExecuteAsync(job.OperationId, "lease", CancellationToken.None);

        Assert.Equal(ImageStatus.Ready, template.Status);
        Assert.Empty(await context.ImageDistributionRecords.ToArrayAsync());
        Assert.Empty(await context.ImageDistributionReferences.ToArrayAsync());
        staging.Verify(item => item.DeleteAsync(job.StagedPath, CancellationToken.None), Times.Exactly(2));
        store.Verify(item => item.UpdateProgressAsync(job.OperationId, "lease", "image-ready", 1, 1,
            "image-template", "42", null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
