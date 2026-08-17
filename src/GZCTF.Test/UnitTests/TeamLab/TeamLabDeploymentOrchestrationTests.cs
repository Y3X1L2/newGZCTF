using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.TeamLab.Contracts;
using GZCTF.Services.Fleet;
using GZCTF.Services;
using GZCTF.Services.TeamLab;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabDeploymentOrchestrationTests
{
    [Fact]
    public async Task DeployAsync_PersistsAllSuccessfulCreateIdentitiesBeforeReportingBatchFailure()
    {
        await using var context = CreateContext();
        var (runtime, _) = await SeedRuntimeAsync(context,
            RuntimeAsset("a-fail", TeamLabAssetExecutionStage.Pending, null),
            RuntimeAsset("m-success", TeamLabAssetExecutionStage.Pending, null),
            RuntimeAsset("z-fail", TeamLabAssetExecutionStage.Pending, null));
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.ApplyInfrastructureAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeInfrastructureApplyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeInfrastructureResult.Applied("sha256:infrastructure"));
        executor.Setup(item => item.CreateAssetAsync(
                It.IsAny<Guid>(), It.Is<TeamLabNodeAssetCreateRequest>(request => request.AssetKey == "a-fail"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeAssetCreateResult.Failed("create failed"));
        executor.Setup(item => item.CreateAssetAsync(
                It.IsAny<Guid>(), It.Is<TeamLabNodeAssetCreateRequest>(request =>
                    request.AssetKey == "m-success"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeAssetCreateResult.Created("container-m-success"));
        executor.Setup(item => item.CreateAssetAsync(
                It.IsAny<Guid>(), It.Is<TeamLabNodeAssetCreateRequest>(request => request.AssetKey == "z-fail"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeAssetCreateResult.Failed("second create failed"));
        var writer = new Mock<IOperationalEventWriter>();
        var eventRecorder = new TeamLabEventRecorder(context, writer.Object, new OperationalCorrelation());
        var routes = new TeamLabRouteApplicationService(context, executor.Object, eventRecorder);
        var artifacts = new Mock<ITeamLabArtifactDistribution>();
        var services = new ServiceCollection();
        services.AddSingleton(artifacts.Object);
        await using var provider = services.BuildServiceProvider();
        var deployment = new TeamLabShardDeploymentService(
            context,
            provider.GetRequiredService<IServiceScopeFactory>(),
            executor.Object,
            CreateImageRegistry(),
            routes,
            eventRecorder,
            Mock.Of<ITeamLabDeploymentProgress>(),
            NullLogger<TeamLabShardDeploymentService>.Instance);

        runtime.ExecutionModel = TeamLabExecutionModel.V1;

        var exception = await Assert.ThrowsAsync<TeamLabRuntimeExecutionException>(() =>
            deployment.DeployAsync(runtime, Topology([
                    Asset("a-fail"),
                    Asset("m-success"),
                    Asset("z-fail")
                ], []),
                new Dictionary<string, TeamLabRuntimeOverlayModel>(), CancellationToken.None));

        Assert.Contains("create failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("second create failed", exception.Message, StringComparison.Ordinal);
        context.ChangeTracker.Clear();
        var assets = await context.TeamLabRuntimeAssets.OrderBy(item => item.TopologyKey).ToArrayAsync();
        Assert.Equal(TeamLabAssetExecutionStage.Failed, assets[0].ExecutionStage);
        Assert.Equal("container-m-success", assets[1].RuntimeResourceId);
        Assert.Equal(TeamLabAssetExecutionStage.GuestReady, assets[1].ExecutionStage);
        Assert.Equal(TeamLabAssetExecutionStage.Failed, assets[2].ExecutionStage);
    }

    [Fact]
    public void Capabilities_AdvertiseTheImplementedWindowsVmRuntime()
    {
        var service = new TeamLabTopologyApplicationService(null!, null!, null!, null!,
            new NodeCapacitySnapshotService(null!));

        Assert.True(service.GetCapabilities().Features.WindowsVm);
    }

    [Fact]
    public async Task CleanupAsync_IncludesDeterministicCurrentGenerationVmNameWhenIdentityWasNotPersisted()
    {
        await using var context = CreateContext();
        var vm = RuntimeAsset("database-node", TeamLabAssetExecutionStage.Pending, null);
        vm.Kind = TeamLabResourceKind.Vm;
        var (runtime, _) = await SeedRuntimeAsync(context, vm);
        runtime.ExecutionModel = TeamLabExecutionModel.V1;
        TeamLabNodeCleanupRequest? cleanupRequest = null;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.GetRuntimeInventoryAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyInventory());
        executor.Setup(item => item.CleanupShardAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeCleanupRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, TeamLabNodeCleanupRequest, CancellationToken>((_, request, _) => cleanupRequest = request)
            .ReturnsAsync(TeamLabNodeResult.Ok());
        var writer = new Mock<IOperationalEventWriter>();
        var eventRecorder = new TeamLabEventRecorder(context, writer.Object, new OperationalCorrelation());
        var traffic = new TeamLabTrafficApplicationService(
            context,
            executor.Object,
            Mock.Of<IDistributedLeaseProvider>(),
            Mock.Of<ITeamLabTrafficIngestor>(),
            eventRecorder,
            NullLogger<TeamLabTrafficApplicationService>.Instance);
        var cleanup = new TeamLabRuntimeCleanupService(
            context,
            executor.Object,
            traffic,
            CaptureCleanup(),
            Mock.Of<IPublicUdpGatewayProvider>(),
            eventRecorder,
            RemoteAccess());

        var result = await cleanup.CleanupAsync(runtime, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(cleanupRequest);
        Assert.Contains(
            TeamLabResourceNameFactory.LinuxName($"tl{runtime.Id}-{vm.TopologyKey}"),
            cleanupRequest.VmNames);
    }

    [Fact]
    public async Task CleanupAsync_UsesInventoryOnlyForTheCurrentRuntimeGeneration()
    {
        await using var context = CreateContext();
        var container = RuntimeAsset("worker", TeamLabAssetExecutionStage.Pending, null);
        var (runtime, _) = await SeedRuntimeAsync(context, container);
        runtime.ExecutionModel = TeamLabExecutionModel.V1;
        TeamLabNodeCleanupRequest? cleanupRequest = null;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.CleanupShardAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeCleanupRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, TeamLabNodeCleanupRequest, CancellationToken>((_, request, _) => cleanupRequest = request)
            .ReturnsAsync(TeamLabNodeResult.Ok());
        var writer = new Mock<IOperationalEventWriter>();
        var eventRecorder = new TeamLabEventRecorder(context, writer.Object, new OperationalCorrelation());
        var traffic = new TeamLabTrafficApplicationService(
            context,
            executor.Object,
            Mock.Of<IDistributedLeaseProvider>(),
            Mock.Of<ITeamLabTrafficIngestor>(),
            eventRecorder,
            NullLogger<TeamLabTrafficApplicationService>.Instance);
        var inventory = new TeamLabNodeRuntimeInventory(
            [
                new TeamLabNodeInventoryResource(
                    "container-current", ContainerStableName(runtime.Id, container.TopologyKey, "current"),
                    runtime.Generation, "running"),
                new TeamLabNodeInventoryResource(
                    "container-old-generation", ContainerStableName(runtime.Id, container.TopologyKey, "old"),
                    runtime.Generation - 1, "running"),
                new TeamLabNodeInventoryResource(
                    "container-other-runtime", ContainerStableName(runtime.Id + 1, container.TopologyKey, "other"),
                    runtime.Generation, "running")
            ],
            [],
            [],
            DateTimeOffset.UtcNow);
        executor.Setup(item => item.GetRuntimeInventoryAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventory);
        var cleanup = new TeamLabRuntimeCleanupService(
            context,
            executor.Object,
            traffic,
            CaptureCleanup(),
            Mock.Of<IPublicUdpGatewayProvider>(),
            eventRecorder,
            RemoteAccess());

        var result = await cleanup.CleanupAsync(runtime, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(cleanupRequest);
        Assert.Contains("container-current", cleanupRequest.ContainerIds);
        Assert.DoesNotContain("container-old-generation", cleanupRequest.ContainerIds);
        Assert.DoesNotContain("container-other-runtime", cleanupRequest.ContainerIds);
    }


    [Fact]
    public async Task CleanupAsync_V2WithoutSnapshotFallsBackToLegacyPath()
    {
        await using var context = CreateContext();
        var (runtime, _) = await SeedRuntimeAsync(
            context,
            RuntimeAsset("entry", TeamLabAssetExecutionStage.GuestReady, "teamlab-entry"));
        runtime.ExecutionModel = TeamLabExecutionModel.V2;
        TeamLabNodeCleanupRequest? cleanupRequest = null;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.GetRuntimeInventoryAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyInventory());
        executor.Setup(item => item.CleanupShardAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeCleanupRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, TeamLabNodeCleanupRequest, CancellationToken>((_, request, _) => cleanupRequest = request)
            .ReturnsAsync(TeamLabNodeResult.Ok());
        var writer = new Mock<IOperationalEventWriter>();
        var eventRecorder = new TeamLabEventRecorder(context, writer.Object, new OperationalCorrelation());
        var traffic = new TeamLabTrafficApplicationService(
            context,
            executor.Object,
            Mock.Of<IDistributedLeaseProvider>(),
            Mock.Of<ITeamLabTrafficIngestor>(),
            eventRecorder,
            NullLogger<TeamLabTrafficApplicationService>.Instance);
        var cleanup = new TeamLabRuntimeCleanupService(
            context,
            executor.Object,
            traffic,
            CaptureCleanup(),
            Mock.Of<IPublicUdpGatewayProvider>(),
            eventRecorder,
            RemoteAccess());

        var result = await cleanup.CleanupAsync(runtime, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(cleanupRequest);
        executor.Verify(item => item.CleanupExecutionPlanAsync(
            It.IsAny<Guid>(), It.IsAny<GZCTF.TeamLab.Contracts.Execution.TeamLabExecutionPlanV2>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CleanupAsync_PersistsCleanupPendingBeforePhysicalRollback()
    {
        await using var context = CreateContext();
        var (runtime, _) = await SeedRuntimeAsync(
            context,
            RuntimeAsset("entry", TeamLabAssetExecutionStage.GuestReady, "teamlab-entry"));
        runtime.ExecutionModel = TeamLabExecutionModel.V1;
        var observedPersistedTransition = false;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.GetRuntimeInventoryAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => observedPersistedTransition =
                runtime.Status == TeamLabRuntimeStatus.CleanupPending &&
                context.Entry(runtime).State == EntityState.Unchanged)
            .ReturnsAsync(EmptyInventory());
        executor.Setup(item => item.CleanupShardAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeResult.Ok());
        var writer = new Mock<IOperationalEventWriter>();
        var eventRecorder = new TeamLabEventRecorder(context, writer.Object, new OperationalCorrelation());
        var traffic = new TeamLabTrafficApplicationService(
            context,
            executor.Object,
            Mock.Of<IDistributedLeaseProvider>(),
            Mock.Of<ITeamLabTrafficIngestor>(),
            eventRecorder,
            NullLogger<TeamLabTrafficApplicationService>.Instance);
        var cleanup = new TeamLabRuntimeCleanupService(
            context,
            executor.Object,
            traffic,
            CaptureCleanup(),
            Mock.Of<IPublicUdpGatewayProvider>(),
            eventRecorder,
            RemoteAccess());

        var result = await cleanup.CleanupAsync(runtime, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.True(observedPersistedTransition);
    }

    [Fact]
    public async Task CleanupAsync_CaptureFailureKeepsRuntimeCleanupPending()
    {
        await using var context = CreateContext();
        var (runtime, _) = await SeedRuntimeAsync(
            context,
            RuntimeAsset("entry", TeamLabAssetExecutionStage.GuestReady, "teamlab-entry"));
        runtime.ExecutionModel = TeamLabExecutionModel.V1;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.GetRuntimeInventoryAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyInventory());
        executor.Setup(item => item.CleanupShardAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeResult.Ok());
        var captureCleanup = new Mock<ITeamLabCaptureCleanup>();
        captureCleanup.Setup(item => item.ExpireGenerationAsync(
                runtime.Id, runtime.Generation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Object-storage capture cleanup is pending."]);
        var writer = new Mock<IOperationalEventWriter>();
        var eventRecorder = new TeamLabEventRecorder(context, writer.Object, new OperationalCorrelation());
        var traffic = new TeamLabTrafficApplicationService(
            context,
            executor.Object,
            Mock.Of<IDistributedLeaseProvider>(),
            Mock.Of<ITeamLabTrafficIngestor>(),
            eventRecorder,
            NullLogger<TeamLabTrafficApplicationService>.Instance);
        var cleanup = new TeamLabRuntimeCleanupService(
            context,
            executor.Object,
            traffic,
            captureCleanup.Object,
            Mock.Of<IPublicUdpGatewayProvider>(),
            eventRecorder,
            RemoteAccess());

        var result = await cleanup.CleanupAsync(runtime, markDestroyedOnSuccess: true, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(TeamLabRuntimeStatus.CleanupPending, runtime.Status);
        Assert.False(runtime.IsOpenToPlayers);
        Assert.Contains("Object-storage capture cleanup is pending", runtime.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyGraph_UnlocksIndependentAssetsAndExactDependencyCondition()
    {
        var topology = Topology(
            [Asset("entry", TeamLabHealthCheckKind.Http), Asset("dependent"), Asset("independent")],
            [new TeamLabExecutionDependency(
                "dependent", "entry", TeamLabDependencyCondition.ServiceReady)]);
        var graph = TeamLabDependencyGraph.Compile(topology);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var scheduled = new HashSet<string>(StringComparer.Ordinal);

        Assert.True(graph.TryTakeReadyBatch(completed, scheduled, out var initial));
        Assert.Equal(
            ["entry:create", "independent:create"],
            initial.Select(item => item.Key).ToArray());

        completed.Add("entry:create");
        completed.Add("independent:create");
        Assert.True(graph.TryTakeReadyBatch(completed, scheduled, out var health));
        Assert.DoesNotContain(health, item => item.Key == "dependent:create");

        completed.Add("entry:health");
        Assert.True(graph.TryTakeReadyBatch(completed, scheduled, out var unlocked));
        Assert.Contains(unlocked, item => item.Key == "dependent:create");
    }

    private static ITeamLabCaptureCleanup CaptureCleanup()
    {
        var cleanup = new Mock<ITeamLabCaptureCleanup>();
        cleanup.Setup(item => item.ExpireGenerationAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return cleanup.Object;
    }

    [Fact]
    public void DependencyGraph_RestoresOnlyDurableCompletedStages()
    {
        var completed = TeamLabDependencyGraph.RestoreCompletedNodes(
        [
            RuntimeAsset("ready", TeamLabAssetExecutionStage.ServiceReady, "container-ready"),
            RuntimeAsset("guest", TeamLabAssetExecutionStage.GuestReady, "vm-guest"),
            RuntimeAsset("missing", TeamLabAssetExecutionStage.GuestReady, null),
            RuntimeAsset("failed", TeamLabAssetExecutionStage.Failed, "container-failed")
        ]);

        Assert.Equal(
        [
            "guest:create",
            "ready:create",
            "ready:health"
        ], completed.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void DependencyGraph_SeparatesVmDomainCreationFromGuestReadiness()
    {
        var topology = Topology(
            [Asset("vm") with { Kind = TeamLabAssetKind.Vm }, Asset("container")],
            []);
        var graph = TeamLabDependencyGraph.Compile(topology);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var scheduled = new HashSet<string>(StringComparer.Ordinal);

        Assert.True(graph.TryTakeReadyBatch(completed, scheduled, out var create));
        Assert.Equal(["container:create", "vm:create"], create.Select(item => item.Key).ToArray());

        completed.UnionWith(create.Select(item => item.Key));
        Assert.True(graph.TryTakeReadyBatch(completed, scheduled, out var next));
        Assert.Contains(next, item => item.Key == "vm:guestready");

        var vm = RuntimeAsset("vm", TeamLabAssetExecutionStage.Pending, "tl-vm");
        vm.Kind = TeamLabResourceKind.Vm;
        var restored = TeamLabDependencyGraph.RestoreCompletedNodes([vm]);
        Assert.Contains("vm:create", restored);
        Assert.DoesNotContain("vm:guestready", restored);
    }

    [Fact]
    public async Task DeploymentStageMachine_PersistsStableStageAndBoundsMessage()
    {
        await using var context = CreateContext();
        var ticket = new DeploymentQueueTicket
        {
            Id = Guid.CreateVersion7(),
            Kind = DeploymentQueueKind.TeamLabRuntime,
            TeamLabRuntimeId = 42,
            Status = DeploymentQueueTicketStatus.Running
        };
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var accessor = new DeploymentExecutionContextAccessor();
        using var scope = accessor.Push(new DeploymentExecutionContext(
            Guid.CreateVersion7(), true, ticket.Id));
        var machine = new TeamLabDeploymentStageMachine(context, accessor);

        await machine.SetAsync(
            TeamLabDeploymentStage.AssetBooting,
            new string('x', 700),
            CancellationToken.None);

        Assert.Equal(DeploymentStage.AssetBooting, ticket.Stage);
        Assert.Equal(512, ticket.StageMessage?.Length);
    }

    [Fact]
    public void RecoveryPolicy_RequiresExplicitRebuildForMissingAssets()
    {
        var policy = new TeamLabRuntimeRecoveryPolicy(Options.Create(new TeamLabNetworkConfig
        {
            RecoveryGraceSeconds = 30
        }));
        var now = DateTimeOffset.UtcNow;
        var runtime = new TeamLabRuntime
        {
            Generation = 3,
            Status = TeamLabRuntimeStatus.Deploying
        };
        var ticket = new DeploymentQueueTicket
        {
            Generation = 3,
            StartedAt = now.AddMinutes(-2)
        };
        var stateful = RuntimeAsset("stateful", TeamLabAssetExecutionStage.ServiceReady, "vm-stateful");
        stateful.ImageDigest = "sha256:image";
        var container = RuntimeAsset("container", TeamLabAssetExecutionStage.ServiceReady, "container-runtime");
        container.ImageDigest = "sha256:image";

        var denied = policy.CanRebuildMissingAsset(
            runtime, ticket, stateful, true, true, now);
        var alsoDenied = policy.CanRebuildMissingAsset(
            runtime, ticket, container, true, true, now);

        Assert.False(denied.Allowed);
        Assert.False(alsoDenied.Allowed);
        Assert.Contains("显式重建", denied.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryPolicy_PreservesInfrastructureIdentityAndFailsClosedOnIncompleteFacts()
    {
        var policy = new TeamLabRuntimeRecoveryPolicy(Options.Create(new TeamLabNetworkConfig
        {
            RecoveryGraceSeconds = 5
        }));
        var now = DateTimeOffset.UtcNow;
        var runtime = new TeamLabRuntime
        {
            Generation = 2,
            Status = TeamLabRuntimeStatus.Deploying
        };
        var ticket = new DeploymentQueueTicket
        {
            Generation = 2,
            StartedAt = now.AddMinutes(-1)
        };

        var denied = policy.CanReplayInfrastructure(
            runtime, ticket, true, routeFactsComplete: false, accessFactsIntact: true, now);
        var allowed = policy.CanReplayInfrastructure(
            runtime, ticket, true, routeFactsComplete: true, accessFactsIntact: true, now);

        Assert.False(denied.Allowed);
        Assert.Contains("digest", denied.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(allowed.Allowed, allowed.Reason);
        Assert.Equal(2, runtime.Generation);
        Assert.Equal(TeamLabRuntimeStatus.Deploying, runtime.Status);
    }

    private static TeamLabExecutionTopology Topology(
        IReadOnlyList<TeamLabExecutionAsset> assets,
        IReadOnlyList<TeamLabExecutionDependency> dependencies) => new(
        2,
        "deployment-dag",
        [],
        [],
        assets,
        [],
        dependencies,
        new TeamLabExecutionObservationPolicy(true, true, TeamLabEndpointObservationMode.Disabled));

    private static TeamLabExecutionAsset Asset(string key, TeamLabHealthCheckKind? healthCheckKind = null) => new(
        key,
        key,
        TeamLabAssetKind.Docker,
        1,
        10,
        128,
        256,
        [],
        null,
        healthCheckKind,
        healthCheckKind is null ? null : 8080,
        0,
        TeamLabEndpointObservationMode.Disabled);

    private static TeamLabRuntimeAsset RuntimeAsset(
        string key,
        TeamLabAssetExecutionStage stage,
        string? resourceId) => new()
    {
        TopologyKey = key,
        Name = key,
        Kind = TeamLabResourceKind.Docker,
        SourceTemplateId = 1,
        Generation = 1,
        ExecutionStage = stage,
        RuntimeResourceId = resourceId
    };

    private static async Task<(TeamLabRuntime Runtime, TeamLabRuntimeShard Shard)> SeedRuntimeAsync(
        AppDbContext context,
        params TeamLabRuntimeAsset[] assets)
    {
        var node = new WorkerNode
        {
            Id = Guid.CreateVersion7(),
            Name = "teamlab-node",
            HostAddress = "127.0.0.1",
            AgentPort = 8080,
            AuthToken = "test",
            Status = NodeStatus.Online,
            TeamLabFabricIp = "10.250.0.2"
        };
        var runtime = new TeamLabRuntime
        {
            Id = 1001,
            Generation = 1,
            Status = TeamLabRuntimeStatus.Deploying
        };
        var shard = new TeamLabRuntimeShard
        {
            Id = 2001,
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            WorkerNode = node,
            WorkerNodeId = node.Id,
            Status = TeamLabRuntimeStatus.Deploying
        };
        runtime.Shards.Add(shard);
        foreach (var asset in assets)
        {
            asset.Runtime = runtime;
            asset.RuntimeId = runtime.Id;
            asset.Generation = runtime.Generation;
            asset.Shard = shard;
            asset.ShardId = shard.Id;
            asset.WorkerNode = node;
            asset.WorkerNodeId = node.Id;
            runtime.Assets.Add(asset);
            shard.Assets.Add(asset);
        }
        runtime.FabricLinkLeases.Add(new TeamLabFabricLinkLease
        {
            Id = 3001,
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            Shard = shard,
            ShardId = shard.Id,
            WorkerNode = node,
            WorkerNodeId = node.Id,
            AllocatedCidr = new IPNetwork(IPAddress.Parse("10.251.0.0"), 30),
            HubAddress = "10.251.0.1/30",
            NodeAddress = "10.251.0.2/30"
        });
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 1,
            Name = "teamlab-test",
            ImageType = ImageType.Docker,
            OSType = OSType.Linux,
            RegistryUrl = "gzctf-internal://teamlab/test:latest",
            ImageHash = "teamlab-test",
            FileSize = 1,
            Status = ImageStatus.Ready
        });
        context.AddRange(node, runtime);
        await context.SaveChangesAsync();
        return (runtime, shard);
    }

    private static TeamLabNodeRuntimeInventory EmptyInventory() => new(
        [],
        [],
        [],
        DateTimeOffset.UtcNow);

    private static ITeamLabRemoteAccessService RemoteAccess()
    {
        var service = new Mock<ITeamLabRemoteAccessService>();
        service.Setup(item => item.EndRuntimeSessionsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return service.Object;
    }

    private static string ContainerStableName(int runtimeId, string assetKey, string suffix)
    {
        var stableId = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(assetKey)), 0) & int.MaxValue;
        return $"gzctf_c{stableId}_tteamlab-{runtimeId}_{suffix}";
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static DockerImageRegistryService CreateImageRegistry()
    {
        var agent = new AgentClient(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance);
        return new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings()),
            Mock.Of<IServiceScopeFactory>(),
            agent,
            NullLogger<DockerImageRegistryService>.Instance);
    }
}
