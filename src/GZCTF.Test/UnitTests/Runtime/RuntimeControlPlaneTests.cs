using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class RuntimeControlPlaneTests
{
    [Fact]
    public async Task Queue_DeduplicatesActiveSubject()
    {
        await using var context = CreateContext();
        var service = CreateQueue(context);
        var request = DeploymentQueueRequest.GameContainer(1, 2, 3);

        var first = await service.EnqueueAsync(request, CancellationToken.None);
        var second = await service.EnqueueAsync(request, CancellationToken.None);

        Assert.Equal(first.TicketId, second.TicketId);
        Assert.True(second.ReusedExistingTicket);
        Assert.Single(context.DeploymentQueueTickets);
        var eventCodes = await context.OperationalEvents
            .Where(item => item.DeploymentTicketId == first.TicketId)
            .Select(item => item.EventCode)
            .ToArrayAsync();
        Assert.Contains(OperationalEventCodes.Runtime.TicketEnqueued, eventCodes);
        Assert.Contains(OperationalEventCodes.Runtime.TicketDuplicate, eventCodes);
    }

    [Fact]
    public async Task Queue_ControlSupersedesUnstartedCreateForSameSubject()
    {
        await using var context = CreateContext();
        var service = CreateQueue(context);
        var create = await service.EnqueueAsync(
            DeploymentQueueRequest.GameContainer(1, 2, 3), CancellationToken.None);

        var stop = await service.EnqueueAsync(DeploymentQueueRequest.GameContainer(1, 2, 3) with
        {
            Operation = RuntimeOperationKind.Stop,
            TargetNodeId = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.NotEqual(create.TicketId, stop.TicketId);
        Assert.Equal(DeploymentQueueTicketStatus.Cancelled,
            (await context.DeploymentQueueTickets.SingleAsync(item => item.Id == create.TicketId)).Status);
        Assert.Equal(DeploymentQueueTicketStatus.Pending,
            (await context.DeploymentQueueTickets.SingleAsync(item => item.Id == stop.TicketId)).Status);
    }

    [Fact]
    public async Task Recovery_ReplaysIdempotentDestroyButFailsClosedForExtend()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, 2);
        var destroy = DeploymentQueueTicket.Create(
            DeploymentQueueRequest.MaintenanceContainer(Guid.NewGuid(), node.Id, "cleanup"));
        destroy.Status = DeploymentQueueTicketStatus.Running;
        destroy.StartedAt = DateTimeOffset.UtcNow.AddHours(-1);
        destroy.ClaimOwner = "lost-worker";
        destroy.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var extend = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 3) with
        {
            Operation = RuntimeOperationKind.Extend,
            TargetNodeId = node.Id,
            ExtensionSeconds = 600
        });
        extend.Status = DeploymentQueueTicketStatus.Running;
        extend.StartedAt = DateTimeOffset.UtcNow.AddHours(-1);
        context.DeploymentQueueTickets.AddRange(destroy, extend);
        await context.SaveChangesAsync();

        var recovered = await CreateReconciliation(context)
            .ReconcileAsync(Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(1, recovered.ReplayedCount);
        Assert.Equal(1, recovered.ConflictCount);
        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, destroy.Status);
        Assert.Equal(DeploymentQueueTicketStatus.Failed, extend.Status);
    }

    [Fact]
    public async Task Scheduler_CreatesOwnedReservationWithoutExecuting()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, maxContainers: 2);
        var queue = CreateQueue(context);
        var queued = await queue.EnqueueAsync(DeploymentQueueRequest.GameContainer(1, 2, 3),
            CancellationToken.None);
        var scheduler = CreateScheduler(context);

        var count = await scheduler.SchedulePendingAsync(CancellationToken.None);

        Assert.Equal(1, count);
        var ticket = await context.DeploymentQueueTickets.SingleAsync();
        Assert.Equal(queued.TicketId, ticket.Id);
        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, ticket.Status);
        Assert.Equal(node.Id, ticket.TargetNodeId);
        var reservation = await context.FleetCapacityReservations.SingleAsync();
        Assert.Equal(ticket.Id, reservation.DeploymentQueueTicketId);
        Assert.Equal(node.Id, reservation.WorkerNodeId);
        Assert.Equal(CapacityReservationStatus.Active, reservation.Status);
        Assert.Equal(0, node.CurrentContainers);
        var eventCodes = await context.OperationalEvents
            .Where(item => item.DeploymentTicketId == ticket.Id)
            .Select(item => item.EventCode)
            .ToArrayAsync();
        Assert.Contains(OperationalEventCodes.Runtime.SchedulingStarted, eventCodes);
        Assert.Contains(OperationalEventCodes.Capacity.Reserved, eventCodes);
        Assert.Contains(OperationalEventCodes.Runtime.SchedulingAssigned, eventCodes);
    }

    [Fact]
    public async Task ConcurrentSchedulers_DoNotOversellNode()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var seed = CreateContext(databaseName);
        SeedNode(seed, maxContainers: 1);
        seed.DeploymentQueueTickets.AddRange(
            DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 1, 1)),
            DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 1)));
        await seed.SaveChangesAsync();

        await using var firstContext = CreateContext(databaseName);
        await using var secondContext = CreateContext(databaseName);
        var results = await Task.WhenAll(
            CreateScheduler(firstContext).SchedulePendingAsync(CancellationToken.None),
            CreateScheduler(secondContext).SchedulePendingAsync(CancellationToken.None));

        await using var verify = CreateContext(databaseName);
        Assert.Equal(1, results.Sum());
        Assert.Single(await verify.FleetCapacityReservations
            .Where(item => item.Status == CapacityReservationStatus.Active).ToListAsync());
        Assert.Equal(1, await verify.DeploymentQueueTickets.CountAsync(item =>
            item.Status == DeploymentQueueTicketStatus.Scheduled));
    }

    [Fact]
    public async Task SchedulingContinuesWhileExecutionIsBlocked()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var seed = CreateContext(databaseName);
        SeedNode(seed, maxContainers: 2);
        seed.DeploymentQueueTickets.Add(DeploymentQueueTicket.Create(
            DeploymentQueueRequest.GameContainer(1, 1, 1)));
        await seed.SaveChangesAsync();
        await CreateScheduler(seed).SchedulePendingAsync(CancellationToken.None);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = BuildExecutionProvider(databaseName, new BlockingExecutor(started, release));
        var execution = provider.GetRequiredService<RuntimeExecutionService>();
        var running = execution.ExecuteScheduledAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await using var enqueueContext = CreateContext(databaseName);
        enqueueContext.DeploymentQueueTickets.Add(DeploymentQueueTicket.Create(
            DeploymentQueueRequest.GameContainer(1, 2, 1)));
        await enqueueContext.SaveChangesAsync();
        var scheduled = await CreateScheduler(enqueueContext).SchedulePendingAsync(CancellationToken.None);

        Assert.Equal(1, scheduled);
        release.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(2));
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task QueueSelector_RotatesOwnersAndHonorsCreateQuota()
    {
        await using var context = CreateContext();
        var olderOwner = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 10, 1));
        olderOwner.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var sameOwner = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 10, 2));
        sameOwner.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var otherOwner = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 20, 1));
        var running = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 10, 3));
        running.Status = DeploymentQueueTicketStatus.Running;
        context.DeploymentQueueTickets.AddRange(olderOwner, sameOwner, otherOwner, running);
        await context.SaveChangesAsync();
        var selector = new RuntimeQueueSelector(context, Options.Create(new RuntimeSchedulingOptions
        {
            SchedulingBatchSize = 2,
            EligibleWindowSize = 10,
            MaxConcurrentCreatesPerTeam = 1
        }));

        var selected = await selector.SelectAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal([otherOwner.Id], selected);
    }

    [Fact]
    public async Task CapacitySnapshot_UsesLargestObservedFactAndAddsOwnedReservations()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, maxContainers: 10);
        node.CurrentContainers = 1;
        context.Containers.AddRange(
            new Container { Id = Guid.NewGuid(), NodeId = node.Id, Status = ContainerStatus.Running },
            new Container { Id = Guid.NewGuid(), NodeId = node.Id, Status = ContainerStatus.Pending });
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 10, 1));
        context.DeploymentQueueTickets.Add(ticket);
        context.FleetCapacityReservations.Add(new FleetCapacityReservation
        {
            DeploymentQueueTicketId = ticket.Id,
            WorkerNodeId = node.Id,
            DockerSlots = 2,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });
        await context.SaveChangesAsync();

        var snapshot = Assert.Single(await new NodeCapacitySnapshotService(context)
            .LoadAsync(CancellationToken.None));

        Assert.Equal(2, snapshot.CurrentDocker);
        Assert.Equal(2, snapshot.ReservedDocker);
        Assert.Equal(4, snapshot.AllocatedDocker);
    }

    [Fact]
    public async Task TeamLabScheduling_LateBindsThreePlusOneCapacityAcrossTwoNodes()
    {
        await using var context = CreateContext();
        var first = SeedTeamLabNode(context, "node-a", 3, "10.251.0.2");
        var second = SeedTeamLabNode(context, "node-b", 1, "10.251.0.3");
        var runtime = new TeamLabRuntime
        {
            Id = 900,
            TopologyReleaseId = Guid.NewGuid(),
            Status = TeamLabRuntimeStatus.Scheduled,
            Networks =
            [
                new TeamLabRuntimeNetwork
                {
                    Generation = 1, TopologyKey = "entry", PlacementGroupKey = "entry",
                    Name = "Entry", Cidr = "10.10.0.0/24", GatewayIp = "10.10.0.1",
                    BridgeName = "tl-entry", IsEntry = true
                },
                new TeamLabRuntimeNetwork
                {
                    Generation = 1, TopologyKey = "core", PlacementGroupKey = "core",
                    Name = "Core", Cidr = "192.168.10.0/24", GatewayIp = "192.168.10.1",
                    BridgeName = "tl-core"
                }
            ]
        };
        runtime.Assets.AddRange(Enumerable.Range(1, 3).Select(index => new TeamLabRuntimeAsset
        {
            Generation = 1, PlacementGroupKey = "entry", TopologyKey = $"entry-{index}",
            Name = $"Entry {index}", Kind = TeamLabResourceKind.Docker
        }));
        runtime.Assets.Add(new TeamLabRuntimeAsset
        {
            Generation = 1, PlacementGroupKey = "core", TopologyKey = "core-1",
            Name = "Core", Kind = TeamLabResourceKind.Docker
        });
        context.TeamLabRuntimes.Add(runtime);
        var queue = CreateQueue(context);
        var queued = await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(runtime.Id, 4, 0),
            CancellationToken.None);

        var scheduled = await CreateScheduler(context).SchedulePendingAsync(CancellationToken.None);

        Assert.Equal(1, scheduled);
        Assert.Equal(2, await context.TeamLabRuntimeShards.CountAsync());
        Assert.Equal(2, await context.FleetCapacityReservations.CountAsync(item =>
            item.DeploymentQueueTicketId == queued.TicketId && item.Status == CapacityReservationStatus.Active));
        Assert.All(await context.TeamLabRuntimeAssets.ToArrayAsync(), asset => Assert.NotNull(asset.WorkerNodeId));
        Assert.Contains(await context.TeamLabRuntimeShards.Select(item => item.WorkerNodeId).ToArrayAsync(),
            id => id == first.Id);
        Assert.Contains(await context.TeamLabRuntimeShards.Select(item => item.WorkerNodeId).ToArrayAsync(),
            id => id == second.Id);
    }

    static RuntimeSchedulingService CreateScheduler(AppDbContext context)
    {
        var lease = new LocalDevelopmentLeaseProvider();
        var schedulingOptions = Options.Create(new RuntimeSchedulingOptions());
        var snapshots = new NodeCapacitySnapshotService(context);
        var eligibility = new NodeEligibilityEvaluator(schedulingOptions);
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var correlation = new OperationalCorrelation();
        var teamLabEvents = new TeamLabEventRecorder(context, writer, correlation);
        return new RuntimeSchedulingService(context,
            new FleetCapacityReservationService(context, lease, snapshots, eligibility, writer,
                NullLogger<FleetCapacityReservationService>.Instance),
            new RuntimeQueueSelector(context, schedulingOptions),
            new TeamLabPhysicalPlacementService(context, lease, snapshots, eligibility, writer, teamLabEvents,
                Options.Create(new TeamLabNetworkConfig())),
            new PollingDeploymentQueueWakeup(), writer, correlation,
            NullLogger<RuntimeSchedulingService>.Instance);
    }

    static DeploymentQueueService CreateQueue(AppDbContext context)
    {
        var lease = new LocalDevelopmentLeaseProvider();
        return new DeploymentQueueService(context,
            new FleetCapacityReservationService(context, lease,
                NullLogger<FleetCapacityReservationService>.Instance),
            new PollingDeploymentQueueWakeup(), NullLogger<DeploymentQueueService>.Instance);
    }

    static RuntimeFactReconciliationService CreateReconciliation(AppDbContext context)
    {
        var lease = new LocalDevelopmentLeaseProvider();
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var capacity = new FleetCapacityReservationService(context, lease,
            new NodeCapacitySnapshotService(context),
            new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions())),
            writer,
            NullLogger<FleetCapacityReservationService>.Instance);
        var agent = new AgentClient(
            new Mock<IHttpClientFactory>().Object,
            new Mock<IServiceScopeFactory>().Object,
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance);
        return new RuntimeFactReconciliationService(
            context,
            agent,
            capacity,
            new PollingDeploymentQueueWakeup(),
            writer,
            NullLogger<RuntimeFactReconciliationService>.Instance);
    }

    static ServiceProvider BuildExecutionProvider(string databaseName, DeploymentExecutionService executor)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IDistributedLeaseProvider, LocalDevelopmentLeaseProvider>();
        services.AddScoped<OperationalCorrelation>();
        services.AddScoped<IOperationalEventWriter, EfOperationalEventWriter>();
        services.AddScoped<FleetCapacityReservationService>();
        services.AddSingleton(executor);
        services.AddSingleton<NodeDispatchLimiter>();
        services.AddSingleton<RuntimeExecutionService>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    static WorkerNode SeedNode(AppDbContext context, int maxContainers)
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "worker",
            HostAddress = "10.0.0.2",
            AuthToken = "token",
            IsLocal = true,
            IsSchedulable = true,
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker,
            MaxContainers = maxContainers,
            MaxVms = 0
        };
        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    static WorkerNode SeedTeamLabNode(AppDbContext context, string name, int maxContainers, string tunnelIp)
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = name,
            HostAddress = tunnelIp,
            AuthToken = "token",
            IsLocal = true,
            IsSchedulable = true,
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker,
            MaxContainers = maxContainers,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabFabricStatus = TeamLabFabricStatus.Healthy,
            TeamLabTunnelIp = tunnelIp
        };
        var manifest = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "1.8.3-test", null, 1,
            [AgentFeatureIds.Docker, AgentFeatureIds.DockerPull, AgentFeatureIds.TeamLabFabric,
                AgentFeatureIds.WireGuard],
            new AgentExecutionLimits(2, 0, 2, 0, 4, 2),
            new AgentHostFacts(8, 16L * 1024 * 1024 * 1024, false, false), DateTimeOffset.UtcNow));
        node.CapabilityManifestJson = manifest.Json;
        node.CapabilityManifestSchemaVersion = 1;
        node.CapabilityHash = manifest.Hash;
        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    static AppDbContext CreateContext(string? databaseName = null) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString()).Options);

    sealed class BlockingExecutor(TaskCompletionSource started, TaskCompletionSource release)
        : DeploymentExecutionService
    {
        public override async Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
            CancellationToken token)
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return DeploymentExecutionResult.Completed();
        }
    }
}
