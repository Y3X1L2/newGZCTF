using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public sealed class ImageSelectedNodeDemandTests
{
    [Fact]
    public async Task EnsureVm_CacheHitTouchesOnlySelectedNode()
    {
        var options = Options();
        await using var context = new AppDbContext(options);
        var (template, selected) = await SeedAsync(context);
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            ImageTemplateId = template.Id, WorkerNodeId = selected.Id,
            ImageHash = template.ImageHash!, ImageType = template.ImageType,
            Status = ImageDistributionStatus.Ready
        });
        await context.SaveChangesAsync();

        var result = await Service(context).EnsureVmTemplateOnNodeAsync(template.Id, selected.Id, default);

        Assert.True(result.Success);
        Assert.Equal(selected.Id, (await context.ImageDistributionRecords.SingleAsync()).WorkerNodeId);
        Assert.Equal(2, await context.WorkerNodes.CountAsync());
    }

    [Theory]
    [InlineData("image.storage_capacity_insufficient", OperationalErrorCategory.Storage, true)]
    [InlineData("image.size_mismatch", OperationalErrorCategory.ImageTransfer, false)]
    [InlineData(null, null, false)]
    public async Task EnsureVm_SelectedNodeFailurePreservesMetadataWithoutOtherCopies(
        string? code, OperationalErrorCategory? category, bool retryable)
    {
        var options = Options();
        await using var context = new AppDbContext(options);
        var (template, selected) = await SeedAsync(context);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ensure = Service(context).EnsureVmTemplateOnNodeAsync(template.Id, selected.Id, timeout.Token);
        await using var workerContext = new AppDbContext(options);
        ImageDistributionRecord? record;
        do
        {
            record = await workerContext.ImageDistributionRecords.SingleOrDefaultAsync(timeout.Token);
            if (record is null) await Task.Delay(10, timeout.Token);
        } while (record is null);
        record.Status = ImageDistributionStatus.Failed;
        record.ErrorMessage = "selected node download capacity rejected";
        record.ErrorCategory = category;
        record.LastErrorCode = code;
        record.Retryable = retryable;
        await workerContext.SaveChangesAsync(timeout.Token);

        var exception = await Assert.ThrowsAsync<AgentClientException>(() => ensure);
        Assert.Equal("selected node download capacity rejected", exception.Message);
        Assert.Equal(category ?? OperationalErrorCategory.ImageTransfer, exception.Error.Category);
        Assert.Equal(code ?? "image.transfer_failed", exception.Error.Code);
        Assert.Equal(retryable, exception.Error.Retryable);
        Assert.Equal(selected.Id, exception.Error.WorkerNodeId);
        Assert.Equal("image.vm.ensure", exception.Error.Operation);
        var ticket = new DeploymentQueueTicket { TargetNodeId = selected.Id, Stage = DeploymentStage.ImagePulling };
        var failure = RuntimeOperationalEvents.Failure(ticket, "runtime.execute", exception);
        ticket.ErrorCategory = failure.Category;
        ticket.ErrorCode = failure.Code;
        ticket.Retryable = failure.Retryable;
        var presentation = TeamLabFailurePresentation.ForRuntime(TeamLabRuntimeStatus.Failed, ticket, Guid.NewGuid());
        Assert.NotNull(presentation);
        Assert.Equal(code ?? "image.transfer_failed", presentation.Code);
        Assert.Equal(category ?? OperationalErrorCategory.ImageTransfer, failure.Category);
        Assert.Equal(retryable, failure.Retryable);
        Assert.Equal(selected.Id, (await context.ImageDistributionRecords.AsNoTracking().SingleAsync()).WorkerNodeId);
        Assert.Equal(2, await context.WorkerNodes.CountAsync());
    }

    [Theory]
    [InlineData("image.storage_capacity_insufficient", OperationalErrorCategory.Storage, true)]
    [InlineData("image.digest_mismatch", OperationalErrorCategory.ImageTransfer, false)]
    public async Task EnsureDocker_SelectedNodeFailurePreservesMetadata(string code,
        OperationalErrorCategory category, bool retryable)
    {
        var options = Options();
        await using var context = new AppDbContext(options);
        var (template, selected) = await SeedAsync(context);
        template.ImageType = ImageType.Docker;
        template.RegistryUrl = "gzctf-internal://lab/test:v1";
        foreach (var node in await context.WorkerNodes.ToArrayAsync()) node.Capabilities = NodeCapability.Docker;
        await context.SaveChangesAsync();
        var registry = new DockerImageRegistryService(Microsoft.Extensions.Options.Options.Create(
                new DockerRegistrySettings { Address = "registry.example:5000" }),
            new ServiceCollection().AddSingleton(context).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            null!, NullLogger<DockerImageRegistryService>.Instance);
        var service = new ImageDistributionService(context, null!, registry, null!, null!,
            new ImageDistributionCoordinator(), new DeploymentExecutionContextAccessor(),
            Mock.Of<IOperationalEventWriter>(), NullLogger<ImageDistributionService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ensure = service.EnsureDockerImageOnNodeAsync(template.RegistryUrl, selected.Id, timeout.Token);
        await using var worker = new AppDbContext(options);
        ImageDistributionRecord? record;
        do
        {
            record = await worker.ImageDistributionRecords.SingleOrDefaultAsync(timeout.Token);
            if (record is null) await Task.Delay(10, timeout.Token);
        } while (record is null);
        record.Status = ImageDistributionStatus.Failed;
        record.ErrorCategory = category;
        record.LastErrorCode = code;
        record.Retryable = retryable;
        record.ErrorMessage = "selected Docker transfer rejected";
        await worker.SaveChangesAsync(timeout.Token);

        var failure = await Assert.ThrowsAsync<AgentClientException>(() => ensure);
        Assert.Equal(category, failure.Error.Category);
        Assert.Equal(code, failure.Error.Code);
        Assert.Equal(retryable, failure.Error.Retryable);
        Assert.Equal(selected.Id, failure.Error.WorkerNodeId);
        Assert.Equal("image.docker.ensure", failure.Error.Operation);
        Assert.Equal("selected Docker transfer rejected", failure.Message);
        Assert.Single(await context.ImageDistributionRecords.AsNoTracking().ToArrayAsync());
    }

    private static ImageDistributionService Service(AppDbContext context) => new(
        context, null!, null!, null!, null!, new ImageDistributionCoordinator(),
        new DeploymentExecutionContextAccessor(), Mock.Of<IOperationalEventWriter>(),
        NullLogger<ImageDistributionService>.Instance);

    private static DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static async Task<(ImageTemplate Template, WorkerNode Selected)> SeedAsync(AppDbContext context)
    {
        var template = new ImageTemplate
        {
            Name = "vm", ImageType = ImageType.Qcow2, Status = ImageStatus.Ready,
            ImageHash = new string('a', 64), FileSize = 1024
        };
        var selected = new WorkerNode
        {
            Name = "selected", Status = NodeStatus.Online, IsSchedulable = true,
            Capabilities = NodeCapability.Kvm, LastHeartbeat = DateTimeOffset.UtcNow
        };
        context.AddRange(template, selected, new WorkerNode
        {
            Name = "unselected", Status = NodeStatus.Online, IsSchedulable = true,
            Capabilities = NodeCapability.Kvm, LastHeartbeat = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return (template, selected);
    }
}
