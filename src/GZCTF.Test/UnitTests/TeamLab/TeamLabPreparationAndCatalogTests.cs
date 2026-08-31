using System;
using System.Linq;
using System.Threading.Tasks;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabPreparationAndCatalogTests
{
    [Fact]
    public void Preparation_ReadyWhenAllImagesReadyOnEligibleNodes()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out var templateId, out var digest);
        SeedNodeAndRecord(context, templateId, digest, ImageDistributionStatus.Ready);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("readyToStart", preparation.State);
        Assert.True(preparation.PlanAvailable);
        Assert.True(preparation.ReadyToStart);
        Assert.Empty(preparation.Blockers);
        var image = Assert.Single(preparation.Images);
        Assert.Equal(templateId, image.TemplateId);
        Assert.Equal(1, image.EligibleNodeCount);
        Assert.Equal(1, image.ReadyNodeCount);
        Assert.Null(image.Failure);
    }

    [Fact]
    public void Preparation_BlockedWhenImageDistributionFailed()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out var templateId, out var digest);
        SeedNodeAndRecord(context, templateId, digest, ImageDistributionStatus.Failed);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("blocked", preparation.State);
        Assert.False(preparation.ReadyToStart);
        Assert.Single(preparation.Blockers);
        var image = Assert.Single(preparation.Images);
        Assert.NotNull(image.Failure);
        Assert.Equal("image_distribution_failed", image.Failure!.Code);
        Assert.False(image.Failure.Retryable);
    }

    [Fact]
    public void Preparation_PreparingWhileImagesPulling()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out var templateId, out var digest);
        SeedNodeAndRecord(context, templateId, digest, ImageDistributionStatus.Pulling);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("preparing", preparation.State);
        Assert.True(preparation.PlanAvailable);
        Assert.False(preparation.ReadyToStart);
        Assert.Equal(1, preparation.Images[0].PreparingNodeCount);
    }

    [Fact]
    public void Preparation_BlockedWithoutEligibleNodes()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out _, out _);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("blocked", preparation.State);
        Assert.False(preparation.PlanAvailable);
        Assert.Contains("没有具备对应能力的可调度节点", preparation.Blockers[0]);
    }

    private static Guid SeedRelease(
        AppDbContext context,
        out int templateId,
        out string digest)
    {
        var topology = new TeamLabTopology
        {
            Id = 1,
            PublicId = Guid.NewGuid(),
            Name = "单服务拓扑",
            OwnerUserId = Guid.NewGuid(),
            Revision = 1,
            SchemaVersion = 2
        };
        context.TeamLabTopologies.Add(topology);
        var definition = new TeamLabTopologyDefinitionModel(
            "单服务拓扑",
            [new TeamLabTopologyNetworkModel("net-a", "net-a", new TeamLabAddressPoolModel("10.20.0.0/24", 28), true)],
            [new TeamLabTopologyAssetModel(
                "svc-1", "服务", TeamLabAssetKind.Docker, 100,
                new TeamLabAssetResourceModel(1, 256, 0), [])],
            []);
        var release = new TeamLabTopologyRelease
        {
            Id = Guid.NewGuid(),
            TopologyId = 1,
            ControlScopeId = null,
            Version = 1,
            SourceRevision = 1,
            SchemaVersion = 2,
            CanonicalJson = TeamLabReleaseCodec.Encode(2, definition),
            ContentHash = "sha256:test"
        };
        context.TeamLabTopologyReleases.Add(release);
        templateId = 100;
        digest = "sha256:abc123";
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 100,
            Name = "web 镜像",
            ImageType = ImageType.Docker,
            Status = ImageStatus.Ready,
            ImageHash = digest
        });
        context.SaveChanges();
        return release.Id;
    }

    private static void SeedNodeAndRecord(
        AppDbContext context,
        int templateId,
        string digest,
        ImageDistributionStatus status)
    {
        context.WorkerNodes.Add(new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "node-a",
            IsSchedulable = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            LastHeartbeat = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
        var node = context.WorkerNodes.Single();
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            Id = Guid.NewGuid(),
            ImageTemplateId = templateId,
            WorkerNodeId = node.Id,
            ImageHash = digest,
            ImageType = ImageType.Docker,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
