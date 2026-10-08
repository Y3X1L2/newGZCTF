using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabImageDemandLifecycleTests
{
    [Fact]
    public async Task PublishAndRepeat_DoNotPrewarmAnyWorker()
    {
        await using var context = Context();
        var owner = Guid.NewGuid();
        context.ImageTemplates.Add(Template());
        context.WorkerNodes.AddRange(Node("one"), Node("two"));
        await context.SaveChangesAsync();
        var authoring = new TeamLabTopologyApplicationService(context, new TeamLabTopologyValidator(), null!,
            new TeamLabControlScopeService(context), new NodeCapacitySnapshotService(context));
        var definition = Definition();
        var draft = await authoring.CreateDraftAsync(new CreateTeamLabTopologyModel(
            definition.Name, definition.Networks, definition.Assets, []), owner, default);
        var topology = await context.TeamLabTopologies.Include(item => item.Networks)
            .Include(item => item.Assets).ThenInclude(item => item.Interfaces).ThenInclude(item => item.Network)
            .SingleAsync(item => item.PublicId == draft.Id);
        var releases = new TeamLabReleaseService(context, new TeamLabTopologyValidator());

        var first = await releases.PublishAsync(topology, draft.Revision, owner, null, default);
        var repeated = await releases.PublishAsync(topology, draft.Revision, owner, null, default);

        Assert.Equal(first.Id, repeated.Id);
        Assert.Single(await context.TeamLabTopologyReleases.ToArrayAsync());
        Assert.Empty(await context.ImageDistributionRecords.ToArrayAsync());
        Assert.Empty(await context.ImageDistributionReferences.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminReadiness_AllowsOnDemandStartWithColdOrFailedUnrelatedCaches(bool failedCopy)
    {
        await using var context = Context();
        var release = await SeedReleaseAsync(context);
        var node = Node("unselected");
        context.WorkerNodes.AddRange(node, Node("another"));
        if (failedCopy) context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            ImageTemplateId = 100, WorkerNodeId = node.Id, ImageHash = new string('a', 64),
            ImageType = ImageType.Docker, Status = ImageDistributionStatus.Failed
        });
        await context.SaveChangesAsync();
        var plans = new Mock<ITeamLabTopologyApplicationService>();
        plans.Setup(item => item.PlanAsync(release.Topology.PublicId, release.Id, It.IsAny<Guid>(), true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabPlanModel(release.Topology.PublicId, release.Id, [], [], [], 0, [], [], "plan"));

        var readiness = await Query(context, plans.Object).GetReleaseReadinessAsync(
            release.Topology.PublicId, release.Id, Guid.NewGuid(), true, default);

        Assert.True(readiness.Ready);
        Assert.Empty(readiness.BlockingReasons);
        Assert.Equal(0, Assert.Single(readiness.Images).ReadyNodeCount);
        Assert.Equal(failedCopy ? 1 : 0, readiness.Images[0].FailedNodeCount);
        Assert.Equal(failedCopy ? 1 : 0, await context.ImageDistributionRecords.CountAsync());
    }

    [Theory]
    [InlineData(ImageStatus.Importing)]
    [InlineData(ImageStatus.Error)]
    [InlineData(ImageStatus.Deleting)]
    public async Task AdminReadiness_BlocksUnavailableSourceBeforePlanning(ImageStatus status)
    {
        await using var context = Context();
        var release = await SeedReleaseAsync(context);
        context.ImageTemplates.Single().Status = status;
        context.WorkerNodes.Add(Node("available"));
        await context.SaveChangesAsync();
        var plans = new Mock<ITeamLabTopologyApplicationService>(MockBehavior.Strict);

        var readiness = await Query(context, plans.Object).GetReleaseReadinessAsync(
            release.Topology.PublicId, release.Id, Guid.NewGuid(), true, default);

        Assert.False(readiness.Ready);
        Assert.Null(readiness.Plan);
        Assert.Contains("尚未就绪", Assert.Single(readiness.BlockingReasons));
        plans.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdminReadiness_BlocksWithoutSchedulableNodes()
    {
        await using var context = Context();
        var release = await SeedReleaseAsync(context);
        var plans = new Mock<ITeamLabTopologyApplicationService>();
        plans.Setup(item => item.PlanAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), true,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TeamLabApiContractException("capability_unavailable", "no placement", 409));

        var readiness = await Query(context, plans.Object).GetReleaseReadinessAsync(
            release.Topology.PublicId, release.Id, Guid.NewGuid(), true, default);

        Assert.False(readiness.Ready);
        Assert.Contains(readiness.BlockingReasons, item => item.Contains("当前没有已接入组网的在线可调度节点"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rollout_CreatesTargetRuntimeWithoutPrewarmingWorkers(bool sourceUnavailable)
    {
        await using var context = Context();
        var release = await SeedReleaseAsync(context);
        var runtime = new TeamLabRuntime { TopologyReleaseId = release.Id, Status = TeamLabRuntimeStatus.Pending };
        var rollout = new TeamLabRollout
        {
            ReleaseId = release.Id, Release = release, AdapterKind = "test",
            PreparationRequested = true, Status = TeamLabRolloutStatus.Preparing,
            Targets = [new TeamLabRolloutTarget { ExternalSubject = "student", DisplayName = "student" }]
        };
        context.AddRange(runtime, rollout, Node("one"), Node("two"));
        if (sourceUnavailable) context.ImageTemplates.Single().Status = ImageStatus.Error;
        await context.SaveChangesAsync();
        var provider = new Mock<ITeamLabRolloutTargetProvider>();
        provider.SetupGet(item => item.AdapterKind).Returns("test");
        provider.Setup(item => item.SynchronizeTargetsAsync(It.IsAny<TeamLabRollout>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        provider.Setup(item => item.ProvisionAsync(It.IsAny<TeamLabRollout>(), It.IsAny<TeamLabRolloutTarget>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabRolloutProvisionResult(runtime.Id, runtime.PublicId, null));
        var coordinator = new TeamLabRolloutCoordinator(context, [provider.Object], null!, null!, null!, null!,
            new LocalDevelopmentLeaseProvider(), NullLogger<TeamLabRolloutCoordinator>.Instance);

        await coordinator.ProcessBatchAsync(1, default);

        Assert.Empty(await context.ImageDistributionRecords.ToArrayAsync());
        Assert.Empty(await context.ImageDistributionReferences.ToArrayAsync());
        if (sourceUnavailable)
        {
            Assert.Equal(TeamLabRolloutStatus.Blocked, rollout.Status);
            Assert.Contains("尚未就绪", rollout.LastError);
            provider.Verify(item => item.ProvisionAsync(It.IsAny<TeamLabRollout>(), It.IsAny<TeamLabRolloutTarget>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        else
        {
            Assert.Equal(TeamLabRolloutStatus.RollingOut, rollout.Status);
            Assert.Equal(runtime.Id, Assert.Single(rollout.Targets).RuntimeId);
            provider.Verify(item => item.ProvisionAsync(It.IsAny<TeamLabRollout>(), It.IsAny<TeamLabRolloutTarget>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    private static TeamLabAdminQueryService Query(AppDbContext context, ITeamLabTopologyApplicationService plans) =>
        new(context, plans, new NodeCapacitySnapshotService(context), new TeamLabEmptyUsageProjectionProvider());

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ImageTemplate Template() => new()
    {
        Id = 100, Name = "web", ImageType = ImageType.Docker,
        Status = ImageStatus.Ready, ImageHash = new string('a', 64)
    };

    private static WorkerNode Node(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Status = NodeStatus.Online, IsSchedulable = true,
        Capabilities = NodeCapability.Docker, LastHeartbeat = DateTimeOffset.UtcNow,
        TeamLabNetworkEnabled = true, TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy
    };

    private static TeamLabTopologyDefinitionModel Definition() => new("web-lab",
        [new TeamLabTopologyNetworkModel("entry", "entry", new TeamLabAddressPoolModel("10.20.0.0/16", 24), true)],
        [new TeamLabTopologyAssetModel("web", "web", TeamLabAssetKind.Docker, 100,
            new TeamLabAssetResourceModel(1, 256, 256),
            [new TeamLabTopologyInterfaceModel("eth0", "entry", 10, true)])], []);

    private static async Task<TeamLabTopologyRelease> SeedReleaseAsync(AppDbContext context)
    {
        var topology = new TeamLabTopology { Name = "web-lab", OwnerUserId = Guid.NewGuid(), Revision = 1 };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology, Version = 1, SourceRevision = 1, SchemaVersion = 2,
            CanonicalJson = TeamLabReleaseCodec.Encode(2, Definition(),
                new System.Collections.Generic.Dictionary<string, string> { ["web"] = new string('a', 64) })
        };
        context.AddRange(topology, release, Template());
        await context.SaveChangesAsync();
        return release;
    }
}
