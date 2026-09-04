using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
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
    public void ExecutionContext_FlowsAcrossDependencyInjectionScopes()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<DeploymentExecutionContextAccessor>()
            .BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<DeploymentExecutionContextAccessor>();
        var second = secondScope.ServiceProvider.GetRequiredService<DeploymentExecutionContextAccessor>();
        var expected = new DeploymentExecutionContext(Guid.NewGuid(), true, Guid.NewGuid(), 3);

        using (first.Push(expected))
            Assert.Equal(expected, second.Current);

        Assert.Null(first.Current);
        Assert.Null(second.Current);
    }

    [Fact]
    public void GuestLifecycleStageComparison_DoesNotTreatNetworkAsBootstrapCompletion()
    {
        Assert.False(RuntimeSignalService.Reached(
            AgentRuntimeSignalStage.NetworkApplied,
            AgentRuntimeSignalStage.BootstrapCompleted));
        Assert.True(RuntimeSignalService.Reached(
            AgentRuntimeSignalStage.ObservationReady,
            AgentRuntimeSignalStage.BootstrapCompleted));
        Assert.False(RuntimeSignalService.Reached(
            AgentRuntimeSignalStage.GuestReady,
            AgentRuntimeSignalStage.NetworkApplied));
    }
    [Fact]
    public void TeamLabResetTicket_MatchesBeforeAndAfterGenerationAdvance()
    {
        Assert.True(DeploymentExecutionService.TeamLabGenerationMatches(RuntimeOperationKind.Reset, 4, 3));
        Assert.True(DeploymentExecutionService.TeamLabGenerationMatches(RuntimeOperationKind.Reset, 4, 4));
        Assert.False(DeploymentExecutionService.TeamLabGenerationMatches(RuntimeOperationKind.Reset, 4, 5));
        Assert.True(DeploymentExecutionService.TeamLabGenerationMatches(RuntimeOperationKind.Destroy, 3, 3));
    }

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
    public async Task Queue_AssignsMonotonicGenerationAcrossCompletedCreates()
    {
        await using var context = CreateContext();
        var service = CreateQueue(context);
        var request = DeploymentQueueRequest.GameContainer(1, 2, 3);
        var first = await service.EnqueueAsync(request, CancellationToken.None);
        var firstTicket = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == first.TicketId);
        firstTicket.Status = DeploymentQueueTicketStatus.Succeeded;
        firstTicket.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        var second = await service.EnqueueAsync(request, CancellationToken.None);
        var secondTicket = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == second.TicketId);

        Assert.Equal(1, firstTicket.Generation);
        Assert.Equal(2, secondTicket.Generation);
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
    public async Task Queue_ControlConflictsWithRunningCreateForSameSubject()
    {
        await using var context = CreateContext();
        var service = CreateQueue(context);
        var create = await service.EnqueueAsync(
            DeploymentQueueRequest.GameContainer(1, 2, 3), CancellationToken.None);
        var running = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == create.TicketId);
        running.Status = DeploymentQueueTicketStatus.Running;
        running.ClaimOwner = "worker-1";
        running.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        await context.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<RuntimeApiContractException>(() =>
            service.EnqueueAsync(DeploymentQueueRequest.GameContainer(1, 2, 3) with
            {
                Operation = RuntimeOperationKind.Destroy,
                TargetNodeId = Guid.NewGuid()
            }, CancellationToken.None));

        Assert.Equal("runtime_operation_in_progress", conflict.Code);
        Assert.Equal(DeploymentQueueTicketStatus.Running,
            (await context.DeploymentQueueTickets.SingleAsync(item => item.Id == create.TicketId)).Status);
        Assert.Single(await context.DeploymentQueueTickets.ToArrayAsync());
    }

    [Fact]
    public async Task Recovery_CompletesAbsentDestroyButFailsClosedForExtend()
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

        Assert.Equal(1, recovered.RecoveredTicketCount);
        Assert.Equal(1, recovered.ConflictCount);
        Assert.Equal(DeploymentQueueTicketStatus.Succeeded, destroy.Status);
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
    public async Task Scheduler_AssignsCompetitionVmOnlyToRemoteImageDownloadNode()
    {
        await using var context = CreateContext();
        SeedVmNode(context, "local", isLocal: true,
            [AgentFeatureIds.Kvm, AgentFeatureIds.VmDownload]);
        SeedVmNode(context, "remote-kvm-only", isLocal: false,
            [AgentFeatureIds.Kvm]);
        var eligible = SeedVmNode(context, "remote-image-download", isLocal: false,
            [AgentFeatureIds.Kvm, AgentFeatureIds.VmDownload]);
        context.DeploymentQueueTickets.Add(DeploymentQueueTicket.Create(
            DeploymentQueueRequest.Vm(1, Guid.NewGuid(), 2, Guid.NewGuid())));
        await context.SaveChangesAsync();

        var scheduled = await CreateScheduler(context).SchedulePendingAsync(CancellationToken.None);

        Assert.Equal(1, scheduled);
        var ticket = await context.DeploymentQueueTickets.SingleAsync();
        Assert.Equal(eligible.Id, ticket.TargetNodeId);
        Assert.Equal(eligible.Id, (await context.FleetCapacityReservations.SingleAsync()).WorkerNodeId);
    }

    [Fact]
    public async Task CapacityReservation_ExplainsWhyWindowsVmNodesWereRejected()
    {
        await using var context = CreateContext();
        SeedVmNode(context, "local-image-download", isLocal: true,
            [AgentFeatureIds.Kvm, AgentFeatureIds.VmDownload]);
        SeedVmNode(context, "remote-kvm-only", isLocal: false,
            [AgentFeatureIds.Kvm]);
        var lease = new LocalDevelopmentLeaseProvider();
        var service = new FleetCapacityReservationService(
            context,
            lease,
            new NodeCapacitySnapshotService(context),
            new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions())),
            NullLogger<FleetCapacityReservationService>.Instance);

        var result = await service.TryReserveAsync(Guid.NewGuid(), new FleetCapacityRequest(
            NodeCapability.Kvm,
            new WorkloadResourceVector(0, 0, 0, 0, 1),
            RequiredFeatures: [AgentFeatureIds.Kvm, AgentFeatureIds.VmDownload],
            RequireRemote: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("remote_node_required=1", result.Message);
        Assert.Contains("agent_feature_unavailable=1", result.Message);
    }

    [Fact]
    public void ExecutionFailure_PreservesSpecificErrorWrittenByRuntimeExecutor()
    {
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.Vm(
            1, Guid.NewGuid(), 2, Guid.NewGuid()));
        ticket.Stage = DeploymentStage.Failed;
        ticket.ErrorMessage = "Windows image has no enabled fixed-account RDP configuration.";

        var message = RuntimeExecutionService.ResolveFailureMessage(ticket, "Queued VM creation failed.");

        Assert.Equal(ticket.ErrorMessage, message);
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
        await execution.WaitForIdleAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task BlockedExecution_DoesNotStopLaterScheduledTicket()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var seed = CreateContext(databaseName);
        var blockedNode = SeedNode(seed, maxContainers: 1);
        var readyNode = SeedNode(seed, maxContainers: 1);
        var blocked = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 1, 1));
        blocked.Status = DeploymentQueueTicketStatus.Scheduled;
        blocked.TargetNodeId = blockedNode.Id;
        var ready = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 2));
        ready.Status = DeploymentQueueTicketStatus.Scheduled;
        ready.TargetNodeId = readyNode.Id;
        seed.DeploymentQueueTickets.AddRange(blocked, ready);
        await seed.SaveChangesAsync();

        var blockedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new SelectiveBlockingExecutor(blocked.Id, blockedStarted, release, readyCompleted);
        var provider = BuildExecutionProvider(databaseName, executor);
        var execution = provider.GetRequiredService<RuntimeExecutionService>();

        Assert.Equal(2, await execution.ExecuteScheduledAsync(CancellationToken.None));
        await blockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await readyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await using (var verify = CreateContext(databaseName))
        {
            Assert.Equal(DeploymentQueueTicketStatus.Running,
                (await verify.DeploymentQueueTickets.SingleAsync(item => item.Id == blocked.Id)).Status);
            Assert.Equal(DeploymentQueueTicketStatus.Succeeded,
                (await verify.DeploymentQueueTickets.SingleAsync(item => item.Id == ready.Id)).Status);
        }

        release.SetResult();
        await execution.WaitForIdleAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task AgentTimeout_MarksQueueTicketFailed()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var seed = CreateContext(databaseName);
        var node = SeedNode(seed, maxContainers: 1);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 1, 1));
        ticket.Status = DeploymentQueueTicketStatus.Scheduled;
        ticket.TargetNodeId = node.Id;
        seed.DeploymentQueueTickets.Add(ticket);
        await seed.SaveChangesAsync();

        var provider = BuildExecutionProvider(databaseName,
            new ThrowingExecutor(new OperationCanceledException("Agent request timed out.")));
        var execution = provider.GetRequiredService<RuntimeExecutionService>();
        Assert.Equal(1, await execution.ExecuteScheduledAsync(CancellationToken.None));
        await execution.WaitForIdleAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        await using var verify = CreateContext(databaseName);
        var failed = await verify.DeploymentQueueTickets.SingleAsync();
        Assert.Equal(DeploymentQueueTicketStatus.Failed, failed.Status);
        Assert.Equal(DeploymentStage.Failed, failed.Stage);
        Assert.Equal(OperationalErrorCodes.AgentTimeout, failed.ErrorCode);
        Assert.NotNull(failed.CompletedAt);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task ExecutorFailure_MarksQueueTicketFailed()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var seed = CreateContext(databaseName);
        var node = SeedNode(seed, maxContainers: 1);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 1, 1));
        ticket.Status = DeploymentQueueTicketStatus.Scheduled;
        ticket.TargetNodeId = node.Id;
        seed.DeploymentQueueTickets.Add(ticket);
        await seed.SaveChangesAsync();

        var provider = BuildExecutionProvider(databaseName,
            new ReturningExecutor(DeploymentExecutionResult.Failed("Image pull failed.")));
        var execution = provider.GetRequiredService<RuntimeExecutionService>();
        Assert.Equal(1, await execution.ExecuteScheduledAsync(CancellationToken.None));
        await execution.WaitForIdleAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        await using var verify = CreateContext(databaseName);
        var failed = await verify.DeploymentQueueTickets.SingleAsync();
        Assert.Equal(DeploymentQueueTicketStatus.Failed, failed.Status);
        Assert.Equal("Image pull failed.", failed.ErrorMessage);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task SuccessfulExecution_CompletesQueueTicket()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var seed = CreateContext(databaseName);
        var node = SeedNode(seed, maxContainers: 1);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 1, 1));
        ticket.Status = DeploymentQueueTicketStatus.Scheduled;
        ticket.TargetNodeId = node.Id;
        seed.DeploymentQueueTickets.Add(ticket);
        await seed.SaveChangesAsync();

        var provider = BuildExecutionProvider(databaseName,
            new ReturningExecutor(DeploymentExecutionResult.Completed()));
        var execution = provider.GetRequiredService<RuntimeExecutionService>();
        Assert.Equal(1, await execution.ExecuteScheduledAsync(CancellationToken.None));
        await execution.WaitForIdleAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        await using var verify = CreateContext(databaseName);
        var succeeded = await verify.DeploymentQueueTickets.SingleAsync();
        Assert.Equal(DeploymentQueueTicketStatus.Succeeded, succeeded.Status);
        Assert.Equal(DeploymentStage.Ready, succeeded.Stage);
        Assert.NotNull(succeeded.CompletedAt);
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
    public async Task QueueSelector_RotatesFairnessKeysAndSerializesSubjects()
    {
        await using var context = CreateContext();
        var firstTeam = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(7, 1, 11));
        firstTeam.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var duplicateSubject = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(7, 1, 11));
        duplicateSubject.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var secondTeam = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(7, 2, 12));
        secondTeam.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        context.DeploymentQueueTickets.AddRange(firstTeam, duplicateSubject, secondTeam);
        await context.SaveChangesAsync();
        var selector = new RuntimeQueueSelector(context, Options.Create(new RuntimeSchedulingOptions
        {
            SchedulingBatchSize = 2,
            EligibleWindowSize = 10,
            MaxConcurrentCreatesPerTeam = 2
        }));

        var selectedIds = await selector.SelectAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var selected = new[] { firstTeam, duplicateSubject, secondTeam }
            .Where(ticket => selectedIds.Contains(ticket.Id))
            .ToArray();

        Assert.Equal(2, selected.Length);
        Assert.Equal(2, selected.Select(ticket => ticket.FairnessKey).Distinct().Count());
        Assert.Equal(2, selected.Select(ticket => ticket.SubjectConcurrencyKey).Distinct().Count());
        Assert.Contains(firstTeam, selected);
        Assert.DoesNotContain(duplicateSubject, selected);
    }

    [Fact]
    public void WorkloadSchedulingIdentity_RejectsEmptyKeys()
    {
        Assert.Throws<ArgumentException>(() => new WorkloadSchedulingIdentity("", "team:1", "runtime:1"));
        Assert.Throws<ArgumentException>(() => new WorkloadSchedulingIdentity("competition:1", " ", "runtime:1"));
        Assert.Throws<ArgumentException>(() => new WorkloadSchedulingIdentity("competition:1", "team:1", ""));
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
    public async Task CapacitySnapshot_DoesNotDoubleCountTeamLabFactsAndTheirReservation()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, maxContainers: 10);
        var runtime = new TeamLabRuntime
        {
            Id = 880,
            TopologyReleaseId = Guid.NewGuid(),
            Generation = 1,
            Status = TeamLabRuntimeStatus.Deploying
        };
        runtime.Assets.Add(new TeamLabRuntimeAsset
        {
            Generation = 1,
            TopologyKey = "entry",
            PlacementGroupKey = "entry",
            Name = "Entry",
            Kind = TeamLabResourceKind.Docker,
            WorkerNodeId = node.Id,
            Status = TeamLabRuntimeStatus.Deploying
        });
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(runtime.Id, 2, 0));
        context.TeamLabRuntimes.Add(runtime);
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

        Assert.Equal(1, snapshot.FactDocker);
        Assert.Equal(1, snapshot.ReservedDocker);
        Assert.Equal(2, snapshot.AllocatedDocker);
    }

    [Fact]
    public async Task TeamLabScheduling_LateBindsThreePlusOneCapacityAcrossTwoNodes()
    {
        await using var context = CreateContext();
        var first = SeedTeamLabNode(context, "node-a", 3, "10.251.0.2");
        var second = SeedTeamLabNode(context, "node-b", 1, "10.251.0.3");
        var releaseId = SeedTeamLabRelease(context,
            Enumerable.Range(1, 3).Select(index => ReleaseAsset($"entry-{index}", "entry"))
                .Append(ReleaseAsset("core-1", "core"))
                .ToArray());
        var runtime = new TeamLabRuntime
        {
            Id = 900,
            TopologyReleaseId = releaseId,
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
        Assert.Equal(2, await context.TeamLabFabricLinkLeases.CountAsync());
        Assert.Equal(4, await context.TeamLabObservationPoints.CountAsync());
    }

    [Fact]
    public async Task TeamLabResetWithoutCurrentShard_RunsOnControlPlaneWithoutWorkerDependency()
    {
        await using var context = CreateContext();
        var runtime = new TeamLabRuntime
        {
            Id = 902,
            Generation = 2,
            TopologyReleaseId = Guid.NewGuid(),
            Status = TeamLabRuntimeStatus.Failed
        };
        context.TeamLabRuntimes.Add(runtime);
        var queue = CreateQueue(context);
        var queued = await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(runtime.Id, 2, 1) with
        {
            Operation = RuntimeOperationKind.Reset,
            Generation = 3
        }, CancellationToken.None);

        var scheduled = await CreateScheduler(context).SchedulePendingAsync(CancellationToken.None);

        Assert.Equal(1, scheduled);
        var ticket = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == queued.TicketId);
        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, ticket.Status);
        Assert.Null(ticket.TargetNodeId);
        Assert.Empty(await context.FleetCapacityReservations
            .Where(item => item.DeploymentQueueTicketId == ticket.Id)
            .ToArrayAsync());
    }

    [Fact]
    public void TeamLabResetPlacement_CreditsResourcesRemovedAfterTheLastHeartbeat()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            MaxContainers = 1,
            MaxVms = 1,
            LastHeartbeat = DateTimeOffset.UtcNow.AddSeconds(-10)
        };
        var cleanupCompletedAt = DateTimeOffset.UtcNow;
        var runtime = new TeamLabRuntime { Generation = 2 };
        runtime.Assets.Add(new TeamLabRuntimeAsset
        {
            Generation = 1,
            WorkerNodeId = node.Id,
            Kind = TeamLabResourceKind.Docker,
            Status = TeamLabRuntimeStatus.Destroyed,
            ExecutionUpdatedAt = cleanupCompletedAt
        });
        runtime.Assets.Add(new TeamLabRuntimeAsset
        {
            Generation = 1,
            WorkerNodeId = node.Id,
            Kind = TeamLabResourceKind.Vm,
            Status = TeamLabRuntimeStatus.Destroyed,
            ExecutionUpdatedAt = cleanupCompletedAt
        });
        var snapshot = new NodeCapacitySnapshot(node, 1, 1, 0, 0, 0, 0);

        var credited = Assert.Single(TeamLabPhysicalPlacementService.ApplyCompletedGenerationCredits(
            runtime, [snapshot]));

        Assert.Equal(1, credited.AvailableDocker);
        Assert.Equal(1, credited.AvailableVm);
    }

    [Fact]
    public async Task TeamLabResetPlacement_ReusesExactPreviousPlacementDespiteStaleDynamicLoad()
    {
        await using var context = CreateContext();
        var first = SeedTeamLabNode(context, "node-a", 1, "10.251.2.2");
        var second = SeedTeamLabNode(context, "node-b", 1, "10.251.2.3");
        var cleanupCompletedAt = DateTimeOffset.UtcNow;
        foreach (var node in new[] { first, second })
        {
            node.CurrentContainers = 1;
            node.MemoryLoad = 0.99f;
            node.LastHeartbeat = cleanupCompletedAt.AddSeconds(-5);
        }
        var releaseId = SeedTeamLabRelease(context,
            ReleaseAsset("entry-asset", "entry"),
            ReleaseAsset("core-asset", "core"));

        var previousEntry = RuntimeNetwork("entry", true);
        previousEntry.WorkerNodeId = first.Id;
        var previousCore = RuntimeNetwork("core", false);
        previousCore.WorkerNodeId = second.Id;
        var currentEntry = RuntimeNetwork("entry", true);
        currentEntry.Generation = 2;
        currentEntry.BridgeName = "tl-entry-g2";
        var currentCore = RuntimeNetwork("core", false);
        currentCore.Generation = 2;
        currentCore.BridgeName = "tl-core-g2";
        var runtime = new TeamLabRuntime
        {
            Id = 903,
            Generation = 2,
            TopologyReleaseId = releaseId,
            Status = TeamLabRuntimeStatus.Scheduled,
            Networks = [previousEntry, previousCore, currentEntry, currentCore]
        };
        runtime.Assets.AddRange([
            PreviousAsset("entry", first.Id, cleanupCompletedAt),
            PreviousAsset("core", second.Id, cleanupCompletedAt),
            CurrentAsset("entry"),
            CurrentAsset("core")]);
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        var queue = CreateQueue(context);
        var queued = await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(runtime.Id, 2, 0) with
        {
            Operation = RuntimeOperationKind.Reset,
            Generation = 2
        }, CancellationToken.None);

        var reservation = await CreateTeamLabPlacement(context)
            .BindAndReserveAsync(queued.TicketId, runtime.Id, CancellationToken.None);

        Assert.True(reservation.Success, reservation.Message);
        var currentPlacements = await context.TeamLabRuntimeNetworks
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == 2)
            .ToDictionaryAsync(item => item.PlacementGroupKey, item => item.WorkerNodeId);
        Assert.Equal(first.Id, currentPlacements["entry"]);
        Assert.Equal(second.Id, currentPlacements["core"]);
    }

    [Fact]
    public void NodeEligibility_DynamicLoadBypassMustBeExplicit()
    {
        var node = new WorkerNode
        {
            Status = NodeStatus.Online,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker,
            MaxContainers = 1,
            MemoryLoad = 0.99f,
            LastHeartbeat = DateTimeOffset.UtcNow
        };
        var snapshot = new NodeCapacitySnapshot(node, 0, 0, 0, 0, 0, 0);
        var eligibility = new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions()));

        Assert.Equal("node_memory_overloaded",
            eligibility.GetReason(snapshot, NodeCapability.Docker, 1, 0, requireTeamLab: false));
        Assert.Null(eligibility.GetReason(snapshot, NodeCapability.Docker, 1, 0,
            requireTeamLab: false, ignoreDynamicLoad: true));
    }

    [Fact]
    public async Task TeamLabScheduling_MinimizesManagedRouterCrossNodeEdges()
    {
        await using var context = CreateContext();
        SeedTeamLabNode(context, "node-a", 2, "10.251.1.2");
        SeedTeamLabNode(context, "node-b", 1, "10.251.1.3");
        var releaseId = SeedTeamLabRelease(context,
            ReleaseAsset("entry-asset", "entry"),
            ReleaseAsset("core-asset", "core"),
            ReleaseAsset("data-asset", "data"));
        var runtime = new TeamLabRuntime
        {
            Id = 901,
            TopologyReleaseId = releaseId,
            Status = TeamLabRuntimeStatus.Scheduled,
            Networks =
            [
                RuntimeNetwork("entry", true),
                RuntimeNetwork("core", false),
                RuntimeNetwork("data", false)
            ],
            Infrastructure =
            [
                new TeamLabRuntimeInfrastructure
                {
                    Generation = 1,
                    TopologyKey = "managed-router",
                    Name = "Managed Router",
                    Kind = TeamLabInfrastructureKind.ManagedRouter,
                    InterfaceSummaryJson = JsonSerializer.Serialize(new[]
                    {
                        new TeamLabRuntimeInfrastructureInterfaceIntent("entry-if", "entry", 1, true),
                        new TeamLabRuntimeInfrastructureInterfaceIntent("core-if", "core", 1, false),
                        new TeamLabRuntimeInfrastructureInterfaceIntent("data-if", "data", 1, false)
                    }),
                    ConnectionSummaryJson = JsonSerializer.Serialize(new[]
                    {
                        new TeamLabRuntimeInfrastructureConnectionIntent(
                            "entry", "core", TeamLabConnectionDirection.Bidirectional),
                        new TeamLabRuntimeInfrastructureConnectionIntent(
                            "core", "data", TeamLabConnectionDirection.FromTo)
                    })
                }
            ]
        };
        foreach (var key in new[] { "entry", "core", "data" })
        {
            runtime.Assets.Add(new TeamLabRuntimeAsset
            {
                Generation = 1,
                PlacementGroupKey = key,
                TopologyKey = $"{key}-asset",
                Name = key,
                Kind = TeamLabResourceKind.Docker
            });
        }
        context.TeamLabRuntimes.Add(runtime);
        var queue = CreateQueue(context);
        await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(runtime.Id, 3, 0), CancellationToken.None);

        Assert.Equal(1, await CreateScheduler(context).SchedulePendingAsync(CancellationToken.None));

        var placements = await context.TeamLabRuntimeNetworks
            .ToDictionaryAsync(item => item.TopologyKey, item => item.WorkerNodeId);
        Assert.Equal(placements["entry"], placements["core"]);
        Assert.NotEqual(placements["core"], placements["data"]);
        Assert.Equal(2, await context.TeamLabRuntimeInfrastructureFragments.CountAsync());
        Assert.Equal(2, await context.TeamLabFabricLinkLeases.CountAsync());
        Assert.Equal(7, await context.TeamLabObservationPoints.CountAsync());
    }

    static TeamLabRuntimeNetwork RuntimeNetwork(string key, bool entry) => new()
    {
        Generation = 1,
        TopologyKey = key,
        PlacementGroupKey = key,
        Name = key,
        Cidr = key switch
        {
            "entry" => "10.10.0.0/24",
            "core" => "172.20.0.0/24",
            _ => "192.168.30.0/24"
        },
        GatewayIp = key switch
        {
            "entry" => "10.10.0.1",
            "core" => "172.20.0.1",
            _ => "192.168.30.1"
        },
        BridgeName = $"tl-{key}",
        IsEntry = entry
    };

    static TeamLabRuntimeAsset PreviousAsset(string groupKey, Guid nodeId, DateTimeOffset cleanupCompletedAt) =>
        new()
        {
            Generation = 1,
            PlacementGroupKey = groupKey,
            TopologyKey = $"{groupKey}-asset",
            Name = groupKey,
            Kind = TeamLabResourceKind.Docker,
            WorkerNodeId = nodeId,
            Status = TeamLabRuntimeStatus.Destroyed,
            ExecutionUpdatedAt = cleanupCompletedAt
        };

    static TeamLabRuntimeAsset CurrentAsset(string groupKey) => new()
    {
        Generation = 2,
        PlacementGroupKey = groupKey,
        TopologyKey = $"{groupKey}-asset",
        Name = groupKey,
        Kind = TeamLabResourceKind.Docker,
        Status = TeamLabRuntimeStatus.Pending
    };

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
                new TeamLabFabricLinkAllocator(context, Options.Create(new TeamLabNetworkConfig())),
                Options.Create(new TeamLabNetworkConfig()), schedulingOptions),
            new PollingDeploymentQueueWakeup(), writer, correlation, Options.Create(new KvmSettings()),
            NullLogger<RuntimeSchedulingService>.Instance);
    }

    static TeamLabPhysicalPlacementService CreateTeamLabPlacement(AppDbContext context)
    {
        var lease = new LocalDevelopmentLeaseProvider();
        var snapshots = new NodeCapacitySnapshotService(context);
        var eligibility = new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions()));
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var teamLabEvents = new TeamLabEventRecorder(context, writer, new OperationalCorrelation());
        return new TeamLabPhysicalPlacementService(context, lease, snapshots, eligibility, writer, teamLabEvents,
            new TeamLabFabricLinkAllocator(context, Options.Create(new TeamLabNetworkConfig())),
            Options.Create(new TeamLabNetworkConfig()), Options.Create(new RuntimeSchedulingOptions()));
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
            new GZCTF.Modules.TeamLab.Application.TeamLabRuntimeRecoveryPolicy(
                Options.Create(new GZCTF.Models.Internal.TeamLabNetworkConfig())),
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
            TeamLabTunnelIp = tunnelIp,
            TeamLabFabricIp = tunnelIp
        };
        var manifest = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "1.8.3-test", null, 1,
            [AgentFeatureIds.Docker, AgentFeatureIds.DockerPull, AgentFeatureIds.TeamLabInfrastructure,
                AgentFeatureIds.TeamLabFabricLeasedLinks, AgentFeatureIds.TeamLabObservation,
                AgentFeatureIds.WireGuard],
            new AgentExecutionLimits(2, 0, 2, 0, 4, 2),
            new AgentHostFacts(8, 16L * 1024 * 1024 * 1024,
                100L * 1024 * 1024 * 1024, false, false), DateTimeOffset.UtcNow));
        node.CapabilityManifestJson = manifest.Json;
        node.CapabilityManifestSchemaVersion = 1;
        node.CapabilityHash = manifest.Hash;
        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    static Guid SeedTeamLabRelease(AppDbContext context, params TeamLabTopologyAssetModel[] assets)
    {
        var networkKeys = assets.SelectMany(asset => asset.Interfaces)
            .Select(item => item.NetworkKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var definition = new TeamLabTopologyDefinitionModel(
            "runtime-placement-test",
            networkKeys.Select((key, index) => new TeamLabTopologyNetworkModel(
                key, key, new TeamLabAddressPoolModel($"10.{40 + index}.0.0/16", 24),
                string.Equals(key, "entry", StringComparison.Ordinal))).ToArray(),
            assets,
            []);
        var canonical = TeamLabReleaseCodec.Encode(2, definition);
        var topology = new TeamLabTopology
        {
            Name = $"runtime-placement-{Guid.NewGuid():N}",
            OwnerUserId = Guid.NewGuid()
        };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology,
            Version = 1,
            SourceRevision = 1,
            SchemaVersion = 2,
            CanonicalJson = canonical,
            ContentHash = TeamLabReleaseCodec.ComputeContentHash(2, canonical)
        };
        context.TeamLabTopologyReleases.Add(release);
        context.SaveChanges();
        return release.Id;
    }

    static TeamLabTopologyAssetModel ReleaseAsset(
        string key,
        string networkKey,
        TeamLabAssetResourceModel? resources = null) => new(
        key,
        key,
        TeamLabAssetKind.Docker,
        1,
        resources ?? new TeamLabAssetResourceModel(10, 256, 512),
        [new TeamLabTopologyInterfaceModel("eth0", networkKey, 10, true)],
        ExposePort: null);

    static WorkerNode SeedVmNode(
        AppDbContext context,
        string name,
        bool isLocal,
        string[] features)
    {
        var manifest = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "1.8.3-test",
            null,
            AgentCapabilityEvaluator.SupportedManifestSchema,
            features,
            new AgentExecutionLimits(0, 2, 0, 2, 2, 2),
            new AgentHostFacts(8, 16L * 1024 * 1024 * 1024, 0, true, true),
            DateTimeOffset.UtcNow));
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = name,
            HostAddress = "10.24.0.30",
            AuthToken = "token",
            IsLocal = isLocal,
            IsSchedulable = true,
            Status = NodeStatus.Online,
            LastHeartbeat = DateTimeOffset.UtcNow,
            Capabilities = NodeCapability.Kvm,
            MaxVms = 2,
            CapabilityManifestJson = manifest.Json,
            CapabilityManifestSchemaVersion = AgentCapabilityEvaluator.SupportedManifestSchema,
            CapabilityHash = manifest.Hash
        };
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

    sealed class SelectiveBlockingExecutor(
        Guid blockedTicketId,
        TaskCompletionSource blockedStarted,
        TaskCompletionSource release,
        TaskCompletionSource readyCompleted) : DeploymentExecutionService
    {
        public override async Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
            CancellationToken token)
        {
            if (ticket.Id == blockedTicketId)
            {
                blockedStarted.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            else
            {
                readyCompleted.TrySetResult();
            }

            return DeploymentExecutionResult.Completed();
        }
    }

    sealed class ThrowingExecutor(Exception exception) : DeploymentExecutionService
    {
        public override Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
            CancellationToken token) => Task.FromException<DeploymentExecutionResult>(exception);
    }

    sealed class ReturningExecutor(DeploymentExecutionResult result) : DeploymentExecutionService
    {
        public override Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
            CancellationToken token) => Task.FromResult(result);
    }
}
