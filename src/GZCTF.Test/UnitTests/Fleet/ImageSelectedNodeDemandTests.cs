using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task EnsureVm_SelectedNodeTransferFailureReturnsActualReasonWithoutOtherCopies()
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
        await workerContext.SaveChangesAsync(timeout.Token);

        var result = await ensure;

        Assert.False(result.Success);
        Assert.Equal("selected node download capacity rejected", result.Message);
        Assert.Equal(selected.Id, (await context.ImageDistributionRecords.AsNoTracking().SingleAsync()).WorkerNodeId);
        Assert.Equal(2, await context.WorkerNodes.CountAsync());
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
