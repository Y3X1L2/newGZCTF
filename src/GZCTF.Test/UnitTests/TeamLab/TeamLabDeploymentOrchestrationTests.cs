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
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
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
                    request.AssetKey == "m-success" && request.StartCommand == "httpd -f -p 8080"),
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
            routes,
            eventRecorder,
            Mock.Of<ITeamLabDeploymentProgress>(),
            new TeamLabBootstrapOrchestrator());

        var exception = await Assert.ThrowsAsync<TeamLabRuntimeExecutionException>(() =>
            deployment.DeployAsync(runtime, Topology([
                    Asset("a-fail"),
                    Asset("m-success") with { StartCommand = "httpd -f -p 8080" },
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
    public async Task DeployAsync_ScenarioArtifactUsesResolvedTemplateAndSkipsPublishBootstrap()
    {
        await using var context = CreateContext();
        var runtimeAsset = RuntimeAsset("ad-dc", TeamLabAssetExecutionStage.Pending, null);
        runtimeAsset.Kind = TeamLabResourceKind.Vm;
        runtimeAsset.SourceTemplateId = 2;
        var (runtime, _) = await SeedRuntimeAsync(context, runtimeAsset);
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 2,
            Name = "scenario-ad-dc",
            ImageType = ImageType.Qcow2,
            OSType = OSType.Windows,
            ImageHash = new string('a', 64),
            FileSize = 4096,
            Status = ImageStatus.Ready,
            VmRuntimeMode = VmRuntimeMode.Scenario
        });
        await context.SaveChangesAsync();

        TeamLabNodeAssetCreateRequest? createdRequest = null;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.ApplyInfrastructureAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeInfrastructureApplyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeInfrastructureResult.Applied("sha256:infrastructure"));
        executor.Setup(item => item.CreateAssetAsync(
                It.IsAny<Guid>(), It.IsAny<TeamLabNodeAssetCreateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, TeamLabNodeAssetCreateRequest, CancellationToken>((_, request, _) => createdRequest = request)
            .ReturnsAsync(TeamLabNodeAssetCreateResult.Created("scenario-vm"));
        executor.Setup(item => item.WaitForAssetReadyAsync(
                It.IsAny<Guid>(), "scenario-vm", It.IsAny<TeamLabNodeAssetCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeResult.Ok());
        executor.Setup(item => item.ApplyBootstrapAsync(
                It.IsAny<Guid>(), "scenario-vm", It.IsAny<TeamLabNodeAssetCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeBootstrapResult.Completed());
        executor.Setup(item => item.ProbeAssetHealthAsync(
                It.IsAny<Guid>(), "scenario-vm", It.IsAny<TeamLabNodeAssetCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeBootstrapResult.Completed());
        // Deployment ends with an inventory verification pass, so the node must report the VM it
        // just created; without this the run fails inside verification instead of reaching the
        // scenario-template assertions below.
        executor.Setup(item => item.GetRuntimeInventoryAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TeamLabNodeRuntimeInventory(
                [],
                [new TeamLabNodeInventoryResource("scenario-vm", "scenario-vm", runtime.Generation, "running")],
                [],
                DateTimeOffset.UtcNow));
        var writer = new Mock<IOperationalEventWriter>();
        var eventRecorder = new TeamLabEventRecorder(context, writer.Object, new OperationalCorrelation());
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ITeamLabArtifactDistribution>());
        await using var provider = services.BuildServiceProvider();
        var deployment = new TeamLabShardDeploymentService(
            context,
            provider.GetRequiredService<IServiceScopeFactory>(),
            executor.Object,
            new TeamLabRouteApplicationService(context, executor.Object, eventRecorder),
            eventRecorder,
            Mock.Of<ITeamLabDeploymentProgress>(),
            new TeamLabBootstrapOrchestrator());
        var topologyAsset = Asset("ad-dc") with
        {
            Kind = TeamLabAssetKind.Vm,
            ImageTemplateId = 69,
            Bootstrap = new TeamLabExecutionBootstrapReference(
                Guid.NewGuid(), 2, new Dictionary<string, string>()),
            BakeAtPublish = true
        };

        await deployment.DeployAsync(
            runtime,
            Topology([topologyAsset], []),
            new Dictionary<string, TeamLabRuntimeOverlayModel>(),
            CancellationToken.None);

        Assert.NotNull(createdRequest);
        Assert.Equal(2, createdRequest.ImageTemplateId);
        Assert.Null(createdRequest.Bootstrap);
    }

    [Fact]
    public void Capabilities_AdvertiseTheImplementedWindowsVmRuntime()
    {
        var service = new TeamLabTopologyApplicationService(null!, null!, null!, null!, null!);

        Assert.True(service.GetCapabilities().Features.WindowsVm);
    }

    [Fact]
    public void ScenarioPublication_AcceptsOnlyProtectedSecretsForBakeAssets()
    {
        var bakeAsset = Asset("ad-dc") with { BakeAtPublish = true };
        var accepted = TeamLabScenarioBakeService.ValidateScenarioOverlays(
            [bakeAsset],
            [new TeamLabRuntimeOverlayModel(
                "ad-dc",
                null,
                new Dictionary<string, string> { ["safe_mode_password"] = "secret" })]);

        Assert.Single(accepted!);
        Assert.Throws<ApiOperationTerminalException>(() =>
            TeamLabScenarioBakeService.ValidateScenarioOverlays(
                [bakeAsset],
                [new TeamLabRuntimeOverlayModel(
                    "ad-dc",
                    new Dictionary<string, string> { ["MODE"] = "build" },
                    null)]));
    }

    [Fact]
    public async Task CleanupAsync_IncludesDeterministicCurrentGenerationVmNameWhenIdentityWasNotPersisted()
    {
        await using var context = CreateContext();
        var vm = RuntimeAsset("database-node", TeamLabAssetExecutionStage.Pending, null);
        vm.Kind = TeamLabResourceKind.Vm;
        var (runtime, _) = await SeedRuntimeAsync(context, vm);
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
            eventRecorder);

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
            eventRecorder);

        var result = await cleanup.CleanupAsync(runtime, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(cleanupRequest);
        Assert.Contains("container-current", cleanupRequest.ContainerIds);
        Assert.DoesNotContain("container-old-generation", cleanupRequest.ContainerIds);
        Assert.DoesNotContain("container-other-runtime", cleanupRequest.ContainerIds);
    }

    [Fact]
    public async Task CleanupAsync_PersistsCleanupPendingBeforePhysicalRollback()
    {
        await using var context = CreateContext();
        var (runtime, _) = await SeedRuntimeAsync(
            context,
            RuntimeAsset("entry", TeamLabAssetExecutionStage.GuestReady, "teamlab-entry"));
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
            eventRecorder);

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
            eventRecorder);

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
            [Asset("entry"), Asset("dependent"), Asset("independent")],
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
        Assert.True(graph.TryTakeReadyBatch(completed, scheduled, out var bootstrap));
        Assert.DoesNotContain(bootstrap, item => item.Key == "dependent:create");

        completed.Add("entry:bootstrap");
        completed.Add("independent:bootstrap");
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
            "ready:bootstrap",
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
        Assert.Contains(next, item => item.Key == "container:bootstrap");
        Assert.Contains(next, item => item.Key == "vm:guestready");
        Assert.DoesNotContain(next, item => item.Key == "vm:bootstrap");

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
            TeamLabDeploymentStage.BootstrapInjecting,
            new string('x', 700),
            CancellationToken.None);

        Assert.Equal(DeploymentStage.BootstrapInjecting, ticket.Stage);
        Assert.Equal(512, ticket.StageMessage?.Length);
    }

    [Fact]
    public void RecoveryPolicy_DeniesStatefulAssetsAndAllowsCompleteStatelessInputs()
    {
        var policy = new TeamLabRuntimeRecoveryPolicy(Options.Create(new TeamLabNetworkConfig
        {
            EnableStatelessAutoRecovery = true,
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
        stateful.Stateless = false;
        stateful.ImageDigest = "sha256:image";
        var stateless = RuntimeAsset("stateless", TeamLabAssetExecutionStage.ServiceReady, "container-stateless");
        stateless.Stateless = true;
        stateless.ImageDigest = "sha256:image";
        stateless.BootstrapDigest = "sha256:bootstrap";

        var denied = policy.CanRebuildMissingAsset(
            runtime, ticket, stateful, true, true, now);
        var allowed = policy.CanRebuildMissingAsset(
            runtime, ticket, stateless, true, true, now);

        Assert.False(denied.Allowed);
        Assert.Contains("Stateful", denied.Reason, StringComparison.Ordinal);
        Assert.True(allowed.Allowed, allowed.Reason);
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

    private static TeamLabExecutionAsset Asset(string key) => new(
        key,
        key,
        TeamLabAssetKind.Docker,
        1,
        10,
        128,
        256,
        [],
        false,
        null,
        new Dictionary<string, string>(),
        null,
        null,
        null,
        0,
        true,
        null,
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

    private static string ContainerStableName(int runtimeId, string assetKey, string suffix)
    {
        var stableId = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(assetKey)), 0) & int.MaxValue;
        return $"gzctf_c{stableId}_tteamlab-{runtimeId}_{suffix}";
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
