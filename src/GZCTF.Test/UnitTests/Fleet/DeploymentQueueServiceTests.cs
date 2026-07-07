using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using GZCTF.Services.Concurrency;
using GZCTF.Models.Internal;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Channels;
using Xunit;
using TaskStatus = GZCTF.Utils.TaskStatus;

namespace GZCTF.Test.UnitTests.Fleet;

public class DeploymentQueueTicketTests
{
    [Fact]
    public void ActiveIdentity_IsStableForGameContainer()
    {
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(
            gameId: 5,
            teamId: 12,
            challengeId: 9));

        Assert.Equal("game-container:5:12:9", ticket.ActiveIdentity);
    }

    [Fact]
    public void ActiveIdentity_IsStableForTeamLabRuntime()
    {
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(
            gameId: 7,
            teamId: 3,
            runtimeId: 18,
            dockerSlots: 4,
            vmSlots: 2));

        Assert.Equal("teamlab-runtime:7:3:18", ticket.ActiveIdentity);
        Assert.Equal(4, ticket.DockerSlots);
        Assert.Equal(2, ticket.VmSlots);
    }

    [Fact]
    public void StatusModel_DoesNotExposeRawDeploymentPayloadOrSecrets()
    {
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(
            gameId: 1,
            teamId: 2,
            challengeId: 3));
        ticket.DeploymentTarget = new DeploymentTarget
        {
            Payload = "{\"Flag\":\"flag{secret}\",\"RegistryAuth\":\"token\",\"PrivateKey\":\"wg-private\"}"
        };

        var model = DeploymentQueueStatusModel.FromTicket(ticket, queuePosition: 4);
        var text = model.ToString();

        Assert.Equal(4, model.QueuePosition);
        Assert.Equal(3, model.PeopleAhead);
        Assert.DoesNotContain("Payload", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flag{secret}", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wg-private", text, StringComparison.OrdinalIgnoreCase);
    }
}

public class DeploymentQueueServiceTests
{
    [Fact]
    public async Task EnqueueAsync_ReturnsExistingActiveTicketInsteadOfDuplicating()
    {
        await using var context = CreateContext();
        var service = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);
        var request = DeploymentQueueRequest.GameContainer(gameId: 1, teamId: 2, challengeId: 3);

        var first = await service.EnqueueAsync(request, CancellationToken.None);
        var second = await service.EnqueueAsync(request, CancellationToken.None);

        Assert.Equal(first.TicketId, second.TicketId);
        Assert.False(first.ReusedExistingTicket);
        Assert.True(second.ReusedExistingTicket);
        Assert.Equal(1, await context.DeploymentQueueTickets.CountAsync());
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsOneBasedQueuePositionWithinSameKind()
    {
        await using var context = CreateContext();
        var service = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);

        await service.EnqueueAsync(DeploymentQueueRequest.GameContainer(1, 1, 1), CancellationToken.None);
        var second = await service.EnqueueAsync(DeploymentQueueRequest.GameContainer(1, 2, 1), CancellationToken.None);
        await service.EnqueueAsync(DeploymentQueueRequest.Vm(1, Guid.NewGuid(), 1, Guid.NewGuid()), CancellationToken.None);

        var status = await service.GetStatusAsync(second.TicketId, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(2, status.QueuePosition);
        Assert.Equal(1, status.PeopleAhead);
    }

    [Fact]
    public async Task CancelAsync_DoesNotReleaseCapacityForPendingTicket()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, currentContainers: 1, currentVms: 1);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 3));
        ticket.Status = DeploymentQueueTicketStatus.Pending;
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await service.CancelAsync(ticket.Id, "admin cancelled", CancellationToken.None);

        var reloadedNode = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(1, reloadedNode.CurrentContainers);
        Assert.Equal(1, reloadedNode.CurrentVms);
        Assert.Equal(0, reloadedNode.ReservedContainers);
        Assert.Equal(0, reloadedNode.ReservedVms);
        Assert.Equal(DeploymentQueueTicketStatus.Cancelled, ticket.Status);
    }

    [Fact]
    public async Task CancelAsync_ReleasesReservedCapacityForCreatingTicketExactlyOnce()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, currentContainers: 2, currentVms: 1,
            reservedContainers: 2, reservedVms: 1);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(
            gameId: 1,
            teamId: 2,
            runtimeId: 3,
            dockerSlots: 2,
            vmSlots: 1));
        ticket.Status = DeploymentQueueTicketStatus.Creating;
        ticket.TargetNodeId = node.Id;
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await service.CancelAsync(ticket.Id, "admin cancelled", CancellationToken.None);
        await service.CancelAsync(ticket.Id, "admin cancelled again", CancellationToken.None);

        var reloadedNode = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(2, reloadedNode.CurrentContainers);
        Assert.Equal(1, reloadedNode.CurrentVms);
        Assert.Equal(0, reloadedNode.ReservedContainers);
        Assert.Equal(0, reloadedNode.ReservedVms);
        Assert.Equal(DeploymentQueueTicketStatus.Cancelled, ticket.Status);
    }

    static DeploymentQueueService CreateService(AppDbContext context)
    {
        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var capacity = new FleetCapacityReservationService(
            context,
            lockService,
            NullLogger<FleetCapacityReservationService>.Instance);

        return new DeploymentQueueService(context, capacity, NullLogger<DeploymentQueueService>.Instance);
    }

    static WorkerNode SeedNode(AppDbContext context, int currentContainers, int currentVms,
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
            MaxContainers = 10,
            MaxVms = 10,
            CurrentContainers = currentContainers,
            CurrentVms = currentVms,
            ReservedContainers = reservedContainers,
            ReservedVms = reservedVms
        };

        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}

public class NodeExecutionGateTests
{
    [Fact]
    public async Task RunAsync_SerializesOperationsOnSameNode_WhenLimitIsOne()
    {
        var gate = new NodeExecutionGate(
            new NodeExecutionGateOptions { MaxConcurrentOperationsPerNode = 1 },
            NullLogger<NodeExecutionGate>.Instance);
        var nodeId = Guid.NewGuid();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = 0;

        var first = gate.RunAsync(nodeId, async token =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task.WaitAsync(token);
        }, CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = gate.RunAsync(nodeId, _ =>
        {
            Interlocked.Exchange(ref secondEntered, 1);
            return Task.CompletedTask;
        }, CancellationToken.None);

        await Task.Delay(100);
        Assert.Equal(0, Volatile.Read(ref secondEntered));

        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref secondEntered));
    }

    [Fact]
    public async Task RunAsync_AllowsDifferentNodesToRunConcurrently_WhenEachLimitIsOne()
    {
        var gate = new NodeExecutionGate(
            new NodeExecutionGateOptions { MaxConcurrentOperationsPerNode = 1 },
            NullLogger<NodeExecutionGate>.Instance);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        async Task Run(Guid nodeId) => await gate.RunAsync(nodeId, async token =>
        {
            if (Interlocked.Increment(ref started) == 2)
                bothStarted.SetResult();

            await release.Task.WaitAsync(token);
        }, CancellationToken.None);

        var first = Run(Guid.NewGuid());
        var second = Run(Guid.NewGuid());

        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref started));
    }
}

public class DeploymentQueueManagerTests
{
    [Fact]
    public async Task ProcessPendingAsync_AssignsAndExecutesRunnableTicket()
    {
        await using var context = CreateContext();
        var node = SeedDockerNode(context, maxContainers: 2);
        var target = new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Status = TargetStatus.Pending,
            Payload = "{}"
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 10));
        ticket.DeploymentTarget = target;
        ticket.DockerSlots = 1;
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var executor = new RecordingDeploymentExecutionService();
        var queue = CreateQueueManager(context, executor);

        var processed = await queue.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(DeploymentQueueTicketStatus.Completed, ticket.Status);
        Assert.Equal(TargetStatus.Completed, target.Status);
        Assert.Equal(node.Id, ticket.TargetNodeId);
        Assert.Equal(node.Id, target.TargetNodeId);
        Assert.Single(executor.ExecutedTicketIds);
        Assert.Equal(ticket.Id, executor.ExecutedTicketIds[0]);
        Assert.Equal(1, context.WorkerNodes.Single().CurrentContainers);
        Assert.Equal(0, context.WorkerNodes.Single().ReservedContainers);
    }

    [Fact]
    public async Task ProcessPendingAsync_ReleasesReservedCapacity_WhenExecutionFails()
    {
        await using var context = CreateContext();
        var node = SeedDockerNode(context, maxContainers: 2);
        var target = new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Status = TargetStatus.Pending,
            Payload = "{}"
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 10));
        ticket.DeploymentTarget = target;
        ticket.DockerSlots = 1;
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var executor = new RecordingDeploymentExecutionService(success: false, error: "agent create failed");
        var queue = CreateQueueManager(context, executor);

        var processed = await queue.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(DeploymentQueueTicketStatus.Failed, ticket.Status);
        Assert.Equal(TargetStatus.Failed, target.Status);
        Assert.Equal("agent create failed", ticket.ErrorMessage);
        Assert.Equal(0, context.WorkerNodes.Single(n => n.Id == node.Id).CurrentContainers);
        Assert.Equal(0, context.WorkerNodes.Single(n => n.Id == node.Id).ReservedContainers);
    }

    [Fact]
    public async Task ProcessPendingAsync_ReservesTeamLabTicketOnPlannedRuntimeNode()
    {
        await using var context = CreateContext();
        var plannedNode = SeedTeamLabNode(context, maxContainers: 3);
        plannedNode.CurrentContainers = 1;
        var otherNode = SeedTeamLabNode(context, maxContainers: 10);
        var runtime = new TeamLabRuntime
        {
            Id = 12,
            GameId = 5,
            TeamId = 7,
            WorkerNodeId = plannedNode.Id,
            Status = TeamLabRuntimeStatus.Scheduled
        };
        context.TeamLabRuntimes.Add(runtime);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(
            gameId: 5,
            teamId: 7,
            runtimeId: runtime.Id,
            dockerSlots: 2,
            vmSlots: 0));
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var executor = new RecordingDeploymentExecutionService();
        var queue = CreateQueueManager(context, executor);

        var processed = await queue.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(DeploymentQueueTicketStatus.Completed, ticket.Status);
        Assert.Equal(plannedNode.Id, ticket.TargetNodeId);
        Assert.Equal(3, context.WorkerNodes.Single(n => n.Id == plannedNode.Id).CurrentContainers);
        Assert.Equal(0, context.WorkerNodes.Single(n => n.Id == plannedNode.Id).ReservedContainers);
        Assert.Equal(0, context.WorkerNodes.Single(n => n.Id == otherNode.Id).CurrentContainers);
    }

    [Fact]
    public async Task ProcessPendingAsync_ExecutesTicketsOnDifferentNodesConcurrently()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var firstNode = SeedDockerNode(context, maxContainers: 1);
        var secondNode = SeedDockerNode(context, maxContainers: 1);
        var first = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 1, 10));
        var second = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 10));
        context.DeploymentQueueTickets.AddRange(first, second);
        await context.SaveChangesAsync();
        var executor = new BlockingDeploymentExecutionService();
        var queue = CreateQueueManager(databaseName, executor);

        var processing = queue.ProcessPendingAsync(CancellationToken.None);

        await executor.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        executor.ReleaseAll.SetResult();
        var processed = await processing.WaitAsync(TimeSpan.FromSeconds(2));
        await using var verifyContext = CreateContext(databaseName);

        Assert.Equal(2, processed);
        Assert.Equal(2, executor.ExecutedTicketIds.Count);
        Assert.All(await verifyContext.DeploymentQueueTickets.ToListAsync(),
            ticket => Assert.Equal(DeploymentQueueTicketStatus.Completed, ticket.Status));
        Assert.Equal(1, verifyContext.WorkerNodes.Single(n => n.Id == firstNode.Id).CurrentContainers);
        Assert.Equal(1, verifyContext.WorkerNodes.Single(n => n.Id == secondNode.Id).CurrentContainers);
        Assert.Equal(0, verifyContext.WorkerNodes.Single(n => n.Id == firstNode.Id).ReservedContainers);
        Assert.Equal(0, verifyContext.WorkerNodes.Single(n => n.Id == secondNode.Id).ReservedContainers);
    }

    [Fact]
    public async Task ProcessPendingAsync_ReleasesReservedCapacity_WhenTicketDisappearsBeforeExecution()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var node = SeedDockerNode(context, maxContainers: 1);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 1, 10));
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var queue = CreateQueueManager(databaseName, new DeletingDeploymentExecutionService(databaseName));

        var processed = await queue.ProcessPendingAsync(CancellationToken.None);
        await using var verifyContext = CreateContext(databaseName);

        Assert.Equal(1, processed);
        Assert.Empty(await verifyContext.DeploymentQueueTickets.ToListAsync());
        Assert.Equal(0, verifyContext.WorkerNodes.Single(n => n.Id == node.Id).CurrentContainers);
        Assert.Equal(0, verifyContext.WorkerNodes.Single(n => n.Id == node.Id).ReservedContainers);
    }

    static QueueManager CreateQueueManager(AppDbContext context, DeploymentExecutionService executor)
        => CreateQueueManager(context.Database.ProviderName ?? Guid.NewGuid().ToString(), executor, context);

    static QueueManager CreateQueueManager(string databaseName, DeploymentExecutionService executor,
        AppDbContext? sharedContext = null)
    {
        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var services = new ServiceCollection();
        if (sharedContext is not null)
            services.AddSingleton(sharedContext);
        else
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IDistributedLockService>(lockService);
        services.AddScoped(_ => new FleetCapacityReservationService(
            sharedContext ?? _.GetRequiredService<AppDbContext>(),
            lockService,
            NullLogger<FleetCapacityReservationService>.Instance));
        services.AddSingleton(new NodeExecutionGate(
            new NodeExecutionGateOptions { MaxConcurrentOperationsPerNode = 1 },
            NullLogger<NodeExecutionGate>.Instance));
        services.AddSingleton(executor);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return new QueueManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            lockService,
            provider.GetRequiredService<NodeExecutionGate>(),
            NullLogger<QueueManager>.Instance);
    }

    static AppDbContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    static WorkerNode SeedDockerNode(AppDbContext context, int maxContainers)
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "node-a",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker,
            MaxContainers = maxContainers,
            MaxVms = 0
        };

        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    static WorkerNode SeedTeamLabNode(AppDbContext context, int maxContainers)
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "teamlab-node",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            MaxContainers = maxContainers,
            MaxVms = 5,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.2"
        };

        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    sealed class RecordingDeploymentExecutionService(bool success = true, string? error = null)
        : DeploymentExecutionService
    {
        public List<Guid> ExecutedTicketIds { get; } = [];

        public override Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
            CancellationToken token)
        {
            ExecutedTicketIds.Add(ticket.Id);
            return Task.FromResult(success
                ? DeploymentExecutionResult.Completed()
                : DeploymentExecutionResult.Failed(error));
        }
    }

    sealed class BlockingDeploymentExecutionService : DeploymentExecutionService
    {
        int _started;
        public List<Guid> ExecutedTicketIds { get; } = [];
        public TaskCompletionSource BothStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseAll { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
            CancellationToken token)
        {
            lock (ExecutedTicketIds)
                ExecutedTicketIds.Add(ticket.Id);

            if (Interlocked.Increment(ref _started) == 2)
                BothStarted.SetResult();

            await ReleaseAll.Task.WaitAsync(token);
            return DeploymentExecutionResult.Completed();
        }
    }

    sealed class DeletingDeploymentExecutionService(string databaseName) : DeploymentExecutionService
    {
        public override async Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
            CancellationToken token)
        {
            await using var context = CreateContext(databaseName);
            var tracked = await context.DeploymentQueueTickets.FirstAsync(t => t.Id == ticket.Id, token);
            context.DeploymentQueueTickets.Remove(tracked);
            await context.SaveChangesAsync(token);
            throw new InvalidOperationException("This executor should not be reached after ticket deletion.");
        }
    }
}

public class DeploymentExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_FailsGameContainerTicket_WhenBusinessInstanceIsMissing()
    {
        await using var context = CreateContext();
        var service = new DeploymentExecutionService(context, NullLogger<DeploymentExecutionService>.Instance);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(
            gameId: 1,
            teamId: 2,
            challengeId: 3));

        var result = await service.ExecuteAsync(ticket, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Game instance", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Payload", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_FailsTeamLabTicket_WhenRuntimeIsMissing()
    {
        await using var context = CreateContext();
        var service = new DeploymentExecutionService(context, NullLogger<DeploymentExecutionService>.Instance);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(
            gameId: 1,
            teamId: 2,
            runtimeId: 3,
            dockerSlots: 2,
            vmSlots: 1));

        var result = await service.ExecuteAsync(ticket, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("TeamLab runtime", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Payload", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_FailsTeamLabTicket_WhenIdentityDoesNotMatchRuntime()
    {
        await using var context = CreateContext();
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            Id = 3,
            GameId = 99,
            TeamId = 88,
            Status = TeamLabRuntimeStatus.Scheduled
        });
        await context.SaveChangesAsync();
        var service = new DeploymentExecutionService(context, NullLogger<DeploymentExecutionService>.Instance);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(
            gameId: 1,
            teamId: 2,
            runtimeId: 3,
            dockerSlots: 2,
            vmSlots: 1));

        var result = await service.ExecuteAsync(ticket, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("identity", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Payload", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}

public class ContainerOwnerLimitLockTests
{
    [Fact]
    public async Task GameContainerCreation_AcquiresTeamLimitLock()
    {
        await using var context = CreateContext();
        var lockService = new RecordingLockService();
        var repository = CreateGameRepository(context, lockService);
        var user = new UserInfo { Id = Guid.Parse("5449f633-36f7-4984-b0a6-815665fbdc65"), UserName = "player" };
        var team = new Team { Id = 7, Name = "team-a" };
        var game = new Game { Id = 11, Title = "game", ContainerCountLimit = 1 };
        var participation = new Participation { Id = 13, GameId = game.Id, Game = game, TeamId = team.Id, Team = team };
        var challenge = new GameChallenge
        {
            Id = 17,
            GameId = game.Id,
            Game = game,
            Title = "web",
            Type = ChallengeType.StaticContainer,
            IsEnabled = true,
            ContainerImage = "alpine:latest",
            ExposePort = 80
        };
        var instance = new GameInstance
        {
            ParticipationId = participation.Id,
            Participation = participation,
            ChallengeId = challenge.Id,
            Challenge = challenge,
            IsLoaded = true
        };
        context.Users.Add(user);
        context.Teams.Add(team);
        context.Games.Add(game);
        context.Participations.Add(participation);
        context.GameChallenges.Add(challenge);
        context.GameInstances.Add(instance);
        await context.SaveChangesAsync();

        var result = await repository.CreateContainer(instance, team, user, game, CancellationToken.None);

        Assert.Equal(TaskStatus.Success, result.Status);
        Assert.Contains("container-limit:game:11:team:7", lockService.AcquiredKeys);
    }

    [Fact]
    public async Task GameContainerCreation_TreatsActiveTeamQueueTicketAsLimitUsage()
    {
        await using var context = CreateContext();
        var lockService = new RecordingLockService();
        var repository = CreateGameRepository(context, lockService);
        var user = new UserInfo { Id = Guid.Parse("6c7f0b1e-e24b-4abd-9e35-2c1c175617f6"), UserName = "player" };
        var team = new Team { Id = 7, Name = "team-a" };
        var game = new Game { Id = 11, Title = "game", ContainerCountLimit = 1 };
        var participation = new Participation { Id = 13, GameId = game.Id, Game = game, TeamId = team.Id, Team = team };
        var challenge = new GameChallenge
        {
            Id = 17,
            GameId = game.Id,
            Game = game,
            Title = "web",
            Type = ChallengeType.StaticContainer,
            IsEnabled = true,
            ContainerImage = "alpine:latest",
            ExposePort = 80
        };
        var instance = new GameInstance
        {
            ParticipationId = participation.Id,
            Participation = participation,
            ChallengeId = challenge.Id,
            Challenge = challenge,
            IsLoaded = true
        };
        context.Users.Add(user);
        context.Teams.Add(team);
        context.Games.Add(game);
        context.Participations.Add(participation);
        context.GameChallenges.Add(challenge);
        context.GameInstances.Add(instance);
        context.DeploymentQueueTickets.Add(DeploymentQueueTicket.Create(
            DeploymentQueueRequest.GameContainer(game.Id, team.Id, challengeId: 99)));
        await context.SaveChangesAsync();

        var result = await repository.CreateContainer(instance, team, user, game, CancellationToken.None);

        Assert.Equal(TaskStatus.Denied, result.Status);
    }

    [Fact]
    public async Task ExerciseContainerCreation_AcquiresUserLimitLock()
    {
        await using var context = CreateContext();
        var lockService = new RecordingLockService();
        var repository = CreateExerciseRepository(context, lockService);
        var userId = Guid.Parse("fbfdb1f1-1261-48e3-a115-66aedff306e4");
        var user = new UserInfo { Id = userId, UserName = "student" };
        var challenge = new ExerciseChallenge
        {
            Id = 23,
            Title = "practice",
            Type = ChallengeType.StaticContainer,
            IsEnabled = true,
            ContainerImage = "alpine:latest",
            ExposePort = 80
        };
        var instance = new ExerciseInstance
        {
            UserId = user.Id,
            User = user,
            ExerciseId = challenge.Id,
            Exercise = challenge,
            IsLoaded = true
        };
        context.Users.Add(user);
        context.ExerciseChallenges.Add(challenge);
        context.ExerciseInstances.Add(instance);
        await context.SaveChangesAsync();

        var result = await repository.CreateContainer(instance, user, CancellationToken.None);

        Assert.Equal(TaskStatus.Success, result.Status);
        Assert.Contains($"container-limit:exercise:user:{userId}", lockService.AcquiredKeys);
    }

    [Fact]
    public async Task ExerciseContainerCreation_TreatsActiveUserQueueTicketAsLimitUsage()
    {
        await using var context = CreateContext();
        var lockService = new RecordingLockService();
        var repository = CreateExerciseRepository(context, lockService);
        var userId = Guid.Parse("30fe5620-7ef7-4a94-8354-d9344d7b7dbd");
        var user = new UserInfo { Id = userId, UserName = "student" };
        var challenge = new ExerciseChallenge
        {
            Id = 23,
            Title = "practice",
            Type = ChallengeType.StaticContainer,
            IsEnabled = true,
            ContainerImage = "alpine:latest",
            ExposePort = 80
        };
        var instance = new ExerciseInstance
        {
            UserId = user.Id,
            User = user,
            ExerciseId = challenge.Id,
            Exercise = challenge,
            IsLoaded = true
        };
        context.Users.Add(user);
        context.ExerciseChallenges.Add(challenge);
        context.ExerciseInstances.Add(instance);
        context.DeploymentQueueTickets.Add(DeploymentQueueTicket.Create(
            DeploymentQueueRequest.ExerciseContainer(user.Id, challengeId: 99)));
        await context.SaveChangesAsync();

        var result = await repository.CreateContainer(instance, user, CancellationToken.None);

        Assert.Equal(TaskStatus.Denied, result.Status);
    }

    static GameInstanceRepository CreateGameRepository(AppDbContext context, IDistributedLockService lockService)
    {
        var services = CreateCommonServices(context, lockService);
        services.AddSingleton(Mock.Of<ICheatInfoRepository>());
        services.AddSingleton(Mock.Of<IGameEventRepository>(r =>
            r.AddEvent(It.IsAny<GameEvent>(), It.IsAny<CancellationToken>()) == Task.FromResult(new GameEvent())));

        return ActivatorUtilities.CreateInstance<GameInstanceRepository>(services.BuildServiceProvider(), context);
    }

    static ExerciseInstanceRepository CreateExerciseRepository(AppDbContext context, IDistributedLockService lockService)
    {
        var services = CreateCommonServices(context, lockService);
        services.AddSingleton(Mock.Of<IStringLocalizer<Program>>());

        return ActivatorUtilities.CreateInstance<ExerciseInstanceRepository>(services.BuildServiceProvider(), context);
    }

    static ServiceCollection CreateCommonServices(AppDbContext context, IDistributedLockService lockService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IDistributedLockService>(lockService);
        services.AddSingleton<IContainerManager, RecordingContainerManager>();
        services.AddSingleton(Mock.Of<IContainerRepository>());
        services.AddSingleton(Mock.Of<INginxProxySyncService>(s =>
            s.TrySyncNowAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.CompletedTask));
        services.AddSingleton(new DeploymentQueueStateAccessor());
        services.AddSingleton(new DeploymentExecutionContextAccessor());
        services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
        services.AddSingleton<IMemoryCache, MemoryCache>();
        services.AddSingleton(Channel.CreateUnbounded<CacheRequest>().Writer);
        services.AddSingleton<CacheHelper>();
        services.AddSingleton(Options.Create(new ContainerPolicy
        {
            AutoDestroyOnLimitReached = false,
            MaxExerciseContainerCountPerUser = 1,
            DefaultLifetime = 120
        }));
        services.AddSingleton(Options.Create(new DockerRegistrySettings { Address = string.Empty }));
        services.AddSingleton(CreateDockerRegistryService(context));
        services.AddLogging();

        return services;
    }

    static DockerImageRegistryService CreateDockerRegistryService(AppDbContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var agentClient = new AgentClient(
            factory.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance);

        return new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings { Address = string.Empty }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            agentClient,
            NullLogger<DockerImageRegistryService>.Instance);
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    sealed class RecordingLockService : IDistributedLockService
    {
        public List<string> AcquiredKeys { get; } = [];

        public Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null)
        {
            AcquiredKeys.Add(key);
            return Task.FromResult<IDisposable>(new Releaser());
        }

        sealed class Releaser : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    sealed class RecordingContainerManager : IContainerManager
    {
        public Task<Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default) =>
            Task.FromResult<Container?>(new Container
            {
                Id = Guid.NewGuid(),
                Image = config.Image,
                ContainerId = $"container-{Guid.NewGuid():N}",
                Status = ContainerStatus.Running,
                IP = "127.0.0.1",
                Port = config.ExposedPort,
                StartedAt = DateTimeOffset.UtcNow
            });

        public Task DestroyContainerAsync(Container container, CancellationToken token = default) =>
            Task.CompletedTask;
    }
}
