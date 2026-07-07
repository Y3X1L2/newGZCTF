using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Concurrency;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class FleetCapacityReservationServiceTests
{
    [Fact]
    public async Task TryReserveAsync_DoesNotOverbookNodeWhenBatchSlotsExceedRemainingCapacity()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, maxContainers: 3, maxVms: 1);
        var service = CreateService(context);

        var first = await service.TryReserveAsync(
            new FleetCapacityRequest(NodeCapability.Docker, DockerSlots: 2, VmSlots: 0),
            CancellationToken.None);
        var second = await service.TryReserveAsync(
            new FleetCapacityRequest(NodeCapability.Docker, DockerSlots: 2, VmSlots: 0),
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.Equal(node.Id, first.NodeId);
        Assert.False(second.Success);
        Assert.Contains("capacity", second.Message, StringComparison.OrdinalIgnoreCase);

        var reloaded = await context.WorkerNodes.FindAsync([node.Id], CancellationToken.None);
        Assert.Equal(0, reloaded!.CurrentContainers);
        Assert.Equal(2, reloaded.ReservedContainers);
    }

    [Fact]
    public async Task ReleaseAsync_RestoresReservedSlotsWithoutGoingNegative()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, currentContainers: 1, currentVms: 1,
            reservedContainers: 1, reservedVms: 1);
        var service = CreateService(context);

        await service.ReleaseAsync(node.Id, dockerSlots: 2, vmSlots: 2, CancellationToken.None);

        var reloaded = await context.WorkerNodes.FindAsync([node.Id], CancellationToken.None);
        Assert.Equal(1, reloaded!.CurrentContainers);
        Assert.Equal(1, reloaded.CurrentVms);
        Assert.Equal(0, reloaded.ReservedContainers);
        Assert.Equal(0, reloaded.ReservedVms);
    }

    [Fact]
    public async Task ReleaseAsync_RetriesTrackedReleaseAfterConcurrencyConflict()
    {
        await using var context = CreateConcurrencyContext(failOnSaveCall: 1);
        var node = SeedNode(context, currentContainers: 2, currentVms: 1,
            reservedContainers: 1, reservedVms: 1);
        var service = CreateService(context);

        await service.ReleaseAsync(node.Id, dockerSlots: 1, vmSlots: 1, CancellationToken.None);

        var reloaded = await context.WorkerNodes.FindAsync([node.Id], CancellationToken.None);
        Assert.Equal(2, reloaded!.CurrentContainers);
        Assert.Equal(1, reloaded.CurrentVms);
        Assert.Equal(0, reloaded.ReservedContainers);
        Assert.Equal(0, reloaded.ReservedVms);
    }

    [Fact]
    public async Task TryReserveAsync_CountsReservedSlotsEvenWhenHeartbeatReportsLowerCurrentUsage()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, maxContainers: 1, currentContainers: 0);
        var service = CreateService(context);

        var first = await service.TryReserveAsync(
            new FleetCapacityRequest(NodeCapability.Docker, DockerSlots: 1, VmSlots: 0),
            CancellationToken.None);

        node.CurrentContainers = 0;
        await context.SaveChangesAsync();

        var second = await service.TryReserveAsync(
            new FleetCapacityRequest(NodeCapability.Docker, DockerSlots: 1, VmSlots: 0),
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        var reloaded = await context.WorkerNodes.FindAsync([node.Id], CancellationToken.None);
        Assert.Equal(0, reloaded!.CurrentContainers);
        Assert.Equal(1, reloaded.ReservedContainers);
    }

    [Fact]
    public async Task ConfirmAsync_MovesReservedSlotsToCurrentUsage()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, currentContainers: 1, currentVms: 1,
            reservedContainers: 2, reservedVms: 1);
        var service = CreateService(context);

        await service.ConfirmAsync(node.Id, dockerSlots: 1, vmSlots: 1, CancellationToken.None);

        var reloaded = await context.WorkerNodes.FindAsync([node.Id], CancellationToken.None);
        Assert.Equal(2, reloaded!.CurrentContainers);
        Assert.Equal(2, reloaded.CurrentVms);
        Assert.Equal(1, reloaded.ReservedContainers);
        Assert.Equal(0, reloaded.ReservedVms);
    }

    [Fact]
    public async Task FleetManager_TryScheduleWithTargetAsync_UsesAtomicCapacityReservation()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, maxContainers: 1);
        var nodeRepo = new Mock<INodeRepository>();
        nodeRepo.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.ToList());
        nodeRepo.Setup(r => r.GetNodeByIdAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.First(n => n.Id == node.Id));
        nodeRepo.Setup(r => r.GetAllNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.ToList());
        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var queue = new QueueManager(
            CreateScopeFactory(context, lockService),
            lockService,
            CreateNodeExecutionGate(),
            NullLogger<QueueManager>.Instance);
        var capacity = new FleetCapacityReservationService(context, lockService,
            NullLogger<FleetCapacityReservationService>.Instance);
        var queueService = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);
        var manager = new FleetManager(
            queue,
            nodeRepo.Object,
            context,
            capacity,
            queueService,
            NullLogger<FleetManager>.Instance);

        var first = await manager.TryScheduleWithTargetAsync(new DeploymentTarget(), CancellationToken.None);
        var second = await manager.TryScheduleWithTargetAsync(new DeploymentTarget(), CancellationToken.None);

        Assert.False(first.IsQueued);
        Assert.True(second.IsQueued);
        Assert.Equal(0, context.WorkerNodes.Single().CurrentContainers);
        Assert.Equal(1, context.WorkerNodes.Single().ReservedContainers);
    }

    [Fact]
    public async Task FleetManager_QueuesDockerDeploymentWithDurableTicket_WhenCapacityIsExhausted()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, maxContainers: 1, currentContainers: 1);
        var manager = CreateFleetManager(context, node);
        var target = new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Payload = JsonSerializer.Serialize(new ContainerConfig
            {
                GameId = 5,
                TeamId = "12",
                UserId = Guid.Parse("46adf3d5-0ea2-49af-b9ea-e4c1c6f0e36c"),
                ChallengeId = 9,
                Image = "registry.local/web:latest",
                Flag = "flag{must-not-leak}",
                ExposedPort = 80
            })
        };

        var result = await manager.TryScheduleWithTargetAsync(target, CancellationToken.None);

        Assert.True(result.IsQueued);
        Assert.NotNull(result.QueueStatus);
        Assert.Equal(1, result.QueueStatus.QueuePosition);
        Assert.DoesNotContain("flag{must-not-leak}", result.QueueStatus.ToString(), StringComparison.OrdinalIgnoreCase);

        var ticket = Assert.Single(context.DeploymentQueueTickets);
        Assert.Equal(DeploymentQueueKind.GameContainer, ticket.Kind);
        Assert.Equal(DeploymentQueueTicketStatus.Pending, ticket.Status);
        Assert.Equal(target.Id, ticket.DeploymentTargetId);
        Assert.Equal("game-container:5:12:9", ticket.ActiveIdentity);
        Assert.Equal(1, context.WorkerNodes.Single().CurrentContainers);
        Assert.Equal(0, context.WorkerNodes.Single().ReservedContainers);
    }

    [Fact]
    public async Task FleetManager_ReleasesReservedCapacity_WhenAssignedTargetPersistenceFails()
    {
        await using var context = CreateFailingContext(failOnSaveCall: 2);
        var node = SeedNode(context, maxContainers: 1);
        var nodeRepo = new Mock<INodeRepository>();
        nodeRepo.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.ToList());
        nodeRepo.Setup(r => r.GetNodeByIdAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.First(n => n.Id == node.Id));
        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var queue = new QueueManager(
            CreateScopeFactory(context, lockService),
            lockService,
            CreateNodeExecutionGate(),
            NullLogger<QueueManager>.Instance);
        var capacity = new FleetCapacityReservationService(context, lockService,
            NullLogger<FleetCapacityReservationService>.Instance);
        var queueService = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);
        var manager = new FleetManager(
            queue,
            nodeRepo.Object,
            context,
            capacity,
            queueService,
            NullLogger<FleetManager>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.TryScheduleWithTargetAsync(new DeploymentTarget(), CancellationToken.None));

        Assert.Equal(0, context.WorkerNodes.Single().CurrentContainers);
        Assert.Equal(0, context.WorkerNodes.Single().ReservedContainers);
    }

    static FleetCapacityReservationService CreateService(AppDbContext context) =>
        new(context, new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance),
            NullLogger<FleetCapacityReservationService>.Instance);

    static NodeExecutionGate CreateNodeExecutionGate() =>
        new(new NodeExecutionGateOptions(), NullLogger<NodeExecutionGate>.Instance);

    static FleetManager CreateFleetManager(AppDbContext context, WorkerNode node)
    {
        var nodeRepo = new Mock<INodeRepository>();
        nodeRepo.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.ToList());
        nodeRepo.Setup(r => r.GetNodeByIdAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.First(n => n.Id == node.Id));
        nodeRepo.Setup(r => r.GetAllNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.ToList());

        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var queue = new QueueManager(
            CreateScopeFactory(context, lockService),
            lockService,
            CreateNodeExecutionGate(),
            NullLogger<QueueManager>.Instance);
        var capacity = new FleetCapacityReservationService(context, lockService,
            NullLogger<FleetCapacityReservationService>.Instance);
        var queueService = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);

        return new FleetManager(
            queue,
            nodeRepo.Object,
            context,
            capacity,
            queueService,
            NullLogger<FleetManager>.Instance);
    }

    static IServiceScopeFactory CreateScopeFactory(AppDbContext context, IDistributedLockService lockService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(lockService);
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    static AppDbContext CreateFailingContext(int failOnSaveCall)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FailingSaveAppDbContext(options, failOnSaveCall);
    }

    static AppDbContext CreateConcurrencyContext(int failOnSaveCall)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ConcurrencySaveAppDbContext(options, failOnSaveCall);
    }

    static WorkerNode SeedNode(AppDbContext context, int maxContainers = 20, int maxVms = 5,
        int currentContainers = 0, int currentVms = 0,
        int reservedContainers = 0, int reservedVms = 0)
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "node-a",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            MaxContainers = maxContainers,
            MaxVms = maxVms,
            CurrentContainers = currentContainers,
            CurrentVms = currentVms,
            ReservedContainers = reservedContainers,
            ReservedVms = reservedVms,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.2"
        };

        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    sealed class FailingSaveAppDbContext : AppDbContext
    {
        readonly int _failOnSaveCall;
        int _saveCalls;

        public FailingSaveAppDbContext(DbContextOptions<AppDbContext> options, int failOnSaveCall)
            : base(options) =>
            _failOnSaveCall = failOnSaveCall;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCalls++;
            if (_saveCalls == _failOnSaveCall)
                throw new InvalidOperationException("simulated persistence failure");

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    sealed class ConcurrencySaveAppDbContext : AppDbContext
    {
        readonly int _failOnSaveCall;
        int _saveCalls;

        public ConcurrencySaveAppDbContext(DbContextOptions<AppDbContext> options, int failOnSaveCall)
            : base(options) =>
            _failOnSaveCall = failOnSaveCall;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCalls++;
            if (_saveCalls == _failOnSaveCall)
                throw new DbUpdateConcurrencyException("simulated capacity counter conflict");

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
