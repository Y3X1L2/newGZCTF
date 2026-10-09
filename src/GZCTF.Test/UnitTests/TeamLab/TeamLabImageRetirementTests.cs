using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabImageRetirementTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task ArchivedRelease_ReleasesBytesButKeepsImmutableAuditAndSeparateDraftProtection()
    {
        await using var context = Context();
        var release = await SeedAsync(context);
        var canonical = release.CanonicalJson;
        var hash = release.ContentHash;
        var references = References(context);
        Assert.False((await references.CanDeleteAsync(7, default)).Allowed);

        await new TeamLabReleaseService(context, new TeamLabTopologyValidator()).ArchiveAsync(release.Id, default);
        Assert.True((await references.CanDeleteAsync(7, default)).Allowed);
        Assert.Equal(canonical, release.CanonicalJson);
        Assert.Equal(hash, release.ContentHash);

        context.TeamLabTopologyAssets.Add(new TeamLabTopologyAsset
        {
            TopologyId = release.TopologyId, Key = "still-editable", Name = "draft", ImageTemplateId = 7
        });
        await context.SaveChangesAsync();
        Assert.Equal("topology-asset", Assert.Single((await references.CanDeleteAsync(7, default)).References).ResourceType);
    }

    [Theory]
    [InlineData(TeamLabRuntimeStatus.Running)]
    [InlineData(TeamLabRuntimeStatus.Paused)]
    [InlineData(TeamLabRuntimeStatus.Stopped)]
    [InlineData(TeamLabRuntimeStatus.Failed)]
    [InlineData(TeamLabRuntimeStatus.CleanupPending)]
    [InlineData(TeamLabRuntimeStatus.Destroying)]
    public async Task ArchivedRelease_CannotReclaimActiveRuntimeSource(TeamLabRuntimeStatus status)
    {
        await using var context = Context();
        var release = await SeedAsync(context, archived: true);
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            TopologyReleaseId = release.Id, Status = status,
            Assets = [new() { SourceTemplateId = 7, ImageDigest = Digest, Status = status }]
        });
        await context.SaveChangesAsync();
        Assert.Equal("runtime-asset", Assert.Single((await References(context).CanDeleteAsync(7, default)).References).ResourceType);
    }

    [Theory]
    [InlineData(TeamLabRolloutStatus.Draft, false)]
    [InlineData(TeamLabRolloutStatus.Preparing, false)]
    [InlineData(TeamLabRolloutStatus.RollingOut, false)]
    [InlineData(TeamLabRolloutStatus.Ready, false)]
    [InlineData(TeamLabRolloutStatus.Draining, false)]
    [InlineData(TeamLabRolloutStatus.Blocked, false)]
    [InlineData(TeamLabRolloutStatus.Failed, false)]
    [InlineData(TeamLabRolloutStatus.Completed, true)]
    [InlineData(TeamLabRolloutStatus.Archived, true)]
    public async Task ArchivedRelease_ProtectsNonterminalRollout(TeamLabRolloutStatus status, bool allowed)
    {
        await using var context = Context();
        var release = await SeedAsync(context, archived: true);
        context.TeamLabRollouts.Add(new TeamLabRollout { ReleaseId = release.Id, Status = status });
        await context.SaveChangesAsync();
        var result = await References(context).CanDeleteAsync(7, default);
        Assert.Equal(allowed, result.Allowed);
        if (!allowed) Assert.Equal("rollout", Assert.Single(result.References).ResourceType);
    }

    [Fact]
    public async Task ArchivedRelease_ProtectsResetCheckpointUntilTicketEnds()
    {
        await using var context = Context();
        var release = await SeedAsync(context, archived: true);
        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = release.Id, Status = TeamLabRuntimeStatus.Destroyed,
            Assets = [new() { SourceTemplateId = 7, ImageDigest = Digest, Status = TeamLabRuntimeStatus.Destroyed }]
        };
        context.Add(runtime);
        await context.SaveChangesAsync();
        var ticket = new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.TeamLabRuntime, Operation = RuntimeOperationKind.Reset,
            TeamLabRuntimeId = runtime.Id, Status = DeploymentQueueTicketStatus.Running
        };
        context.Add(ticket);
        await context.SaveChangesAsync();
        Assert.False((await References(context).CanDeleteAsync(7, default)).Allowed);
        ticket.Status = DeploymentQueueTicketStatus.Failed;
        await context.SaveChangesAsync();
        Assert.True((await References(context).CanDeleteAsync(7, default)).Allowed);
        // A cancellation changes queue intent before its writer has necessarily stopped.
        ticket.Status = DeploymentQueueTicketStatus.Cancelled;
        ticket.ClaimOwner = "reset-writer";
        ticket.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await context.SaveChangesAsync();
        Assert.False((await References(context).CanDeleteAsync(7, default)).Allowed);
        ticket.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        Assert.True((await References(context).CanDeleteAsync(7, default)).Allowed);
    }

    [Fact]
    public async Task Archive_WithdrawsOnlyOwnPrewarmAndRetainsNewerSharedConsumer()
    {
        await using var context = Context();
        var release = await SeedAsync(context);
        var anotherRelease = new TeamLabTopologyRelease
        {
            TopologyId = release.TopologyId, Version = 2, SchemaVersion = 2,
            CanonicalJson = release.CanonicalJson, ContentHash = release.ContentHash
        };
        var node = new WorkerNode { Name = "fixture" };
        var record = new ImageDistributionRecord
        {
            ImageTemplateId = 7, WorkerNode = node, ImageType = ImageType.Qcow2, ImageHash = Digest,
            Status = ImageDistributionStatus.Ready, References =
            [
                new() { Kind = ImageDistributionReferenceKind.TeamLabRelease, ResourcePublicId = release.Id },
                new() { Kind = ImageDistributionReferenceKind.TeamLabRelease, ResourcePublicId = anotherRelease.Id,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1) },
                new() { Kind = ImageDistributionReferenceKind.TeamLabRuntime, ResourceId = 9 }
            ]
        };
        context.AddRange(anotherRelease, record);
        await context.SaveChangesAsync();
        var distribution = new ImageDistributionService(context, null!, null!, null!, null!,
            new ImageDistributionCoordinator(), new DeploymentExecutionContextAccessor(), Mock.Of<IOperationalEventWriter>(),
            NullLogger<ImageDistributionService>.Instance);
        var service = new TeamLabReleaseService(context, new TeamLabTopologyValidator(),
            new TeamLabReleaseImagePreparationService(context, distribution));

        await service.ArchiveAsync(release.Id, default);
        var archivedAt = release.ArchivedAt;
        await service.ArchiveAsync(release.Id, default);

        Assert.Equal(archivedAt, release.ArchivedAt);
        Assert.Equal(2, await context.ImageDistributionReferences.CountAsync());
        Assert.Equal(ImageDistributionStatus.Ready, record.Status);
        Assert.DoesNotContain(await context.ImageDistributionReferences.ToArrayAsync(), item => item.ResourcePublicId == release.Id);
        Assert.False((await References(context).CanDeleteAsync(7, default)).Allowed);
    }

    [Fact]
    public async Task ArchivedRelease_RejectsPrewarmingAndProjectsRealStartBlocker()
    {
        await using var context = Context();
        var release = await SeedAsync(context, archived: true);
        context.WorkerNodes.Add(new WorkerNode
        {
            Capabilities = NodeCapability.Kvm, Status = NodeStatus.Online, IsLocal = true,
            IsSchedulable = true, MaxVms = 10, TeamLabNetworkEnabled = true, TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy
        });
        await context.SaveChangesAsync();
        var preparation = new TeamLabReleaseImagePreparationService(context, null!);

        var exception = await Assert.ThrowsAsync<TeamLabApiContractException>(() => preparation.QueueAsync(release.Id, default));
        Assert.Equal("release_archived", exception.Code);
        var readiness = await preparation.GetPreparationAsync(release.Id, default);
        Assert.False(readiness.ReadyToStart);
        Assert.False(readiness.PlanAvailable);
        Assert.Equal("blocked", readiness.State);
        Assert.Contains(readiness.Blockers, item => item.Contains("已归档", StringComparison.Ordinal));
    }

    private static ImageTemplateReferenceService References(AppDbContext context) =>
        new([new TeamLabImageTemplateReferenceProvider(context)]);

    private static async Task<TeamLabTopologyRelease> SeedAsync(AppDbContext context, bool archived = false)
    {
        var topology = new TeamLabTopology { Name = "retirement", SchemaVersion = 2 };
        var definition = new TeamLabTopologyDefinitionModel("retirement", [],
            [new("vm", "frozen VM", TeamLabAssetKind.Vm, 7, new(1, 256, 0), [])], []);
        var canonical = TeamLabReleaseCodec.Encode(2, definition,
            new Dictionary<string, string> { ["vm"] = Digest });
        var release = new TeamLabTopologyRelease
        {
            Topology = topology, SchemaVersion = 2, Version = 1, CanonicalJson = canonical,
            ContentHash = TeamLabReleaseCodec.ComputeContentHash(2, canonical), IsArchived = archived
        };
        context.AddRange(release, new ImageTemplate
        {
            Id = 7, Name = "retired VM", ImageType = ImageType.Qcow2, OSType = OSType.Linux,
            ImageHash = Digest, FileSize = 1024, Status = ImageStatus.Ready
        });
        await context.SaveChangesAsync();
        return release;
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
