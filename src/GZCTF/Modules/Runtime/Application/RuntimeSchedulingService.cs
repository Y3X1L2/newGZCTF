using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Runtime.Application;

public sealed class RuntimeSchedulingService(
    AppDbContext context,
    FleetCapacityReservationService capacity,
    RuntimeQueueSelector selector,
    TeamLabPhysicalPlacementService teamLabPlacement,
    IDeploymentQueueWakeup wakeup,
    IOperationalEventWriter events,
    OperationalCorrelation correlation,
    IOptions<KvmSettings> kvmOptions,
    ILogger<RuntimeSchedulingService> logger)
{
    static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(2);

    public async Task<int> SchedulePendingAsync(CancellationToken token)
    {
        await RequeueStaleSchedulingClaimsAsync(token);

        var now = DateTimeOffset.UtcNow;
        var ticketIds = await selector.SelectAsync(now, token);

        var scheduled = 0;
        foreach (var ticketId in ticketIds)
        {
            if (!await TryClaimAsync(context, ticketId, now, token))
                continue;

            var ticket = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == ticketId, token);
            var claimOwner = ticket.ClaimOwner;
            if (string.IsNullOrWhiteSpace(claimOwner))
                continue;
            using var correlationScope = correlation.Begin(ticket.Id);
            using var activity = RuntimeOperationalEvents.StartActivity(ticket, "runtime.schedule");
            ticket.Stage = DeploymentStage.AdmissionChecking;
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Runtime.SchedulingStarted,
                OperationalEventOutcome.Started,
                "Runtime scheduling started."));
            await context.SaveChangesAsync(token);
            PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "started");

            if (!await IsTicketStillDeployableAsync(ticket, token))
            {
                if (!await OwnsSchedulingClaimAsync(context, ticket.Id, claimOwner, token))
                {
                    context.ChangeTracker.Clear();
                    continue;
                }
                ticket.Status = DeploymentQueueTicketStatus.Cancelled;
                ticket.Stage = DeploymentStage.Cancelled;
                ticket.CompletedAt = DateTimeOffset.UtcNow;
                ticket.ErrorMessage = "Deployment queue ticket is not deployable anymore.";
                events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    OperationalEventCodes.Runtime.TicketCancelled,
                    OperationalEventOutcome.Cancelled,
                    "Runtime scheduling cancelled a non-deployable ticket."));
                await context.SaveChangesAsync(token);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "not_deployable");
                continue;
            }

            var reservation = await ReserveCapacityAsync(ticket, token);
            if (!await OwnsSchedulingClaimAsync(context, ticket.Id, claimOwner, token))
            {
                context.ChangeTracker.Clear();
                continue;
            }
            var controlPlaneTicket = IsTeamLabControlPlaneTicket(ticket);
            if (!reservation.Success || (!controlPlaneTicket && reservation.NodeId is null))
            {
                ticket.Status = DeploymentQueueTicketStatus.Pending;
                ticket.Stage = DeploymentStage.CapacityWaiting;
                ticket.BlockedReasonCode = "node_capacity_exhausted";
                ticket.StageMessage = reservation.Message;
                ticket.AttemptCount++;
                ticket.NotBeforeAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, 1 << Math.Min(5, ticket.AttemptCount)));
                ticket.ClaimOwner = null;
                ticket.ClaimExpiresAt = null;
                var error = new OperationalError(
                    OperationalErrorCategory.Capacity,
                    OperationalErrorCodes.RuntimeCapacityExhausted,
                    "No eligible node currently has sufficient capacity.",
                    true,
                    WorkerNodeId: ticket.TargetNodeId,
                    Operation: "runtime.schedule");
                ticket.ErrorCategory = error.Category;
                ticket.ErrorCode = error.Code;
                ticket.Retryable = error.Retryable;
                events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    OperationalEventCodes.Runtime.SchedulingBlocked,
                    OperationalEventOutcome.Blocked,
                    "Runtime scheduling is waiting for node capacity.",
                    OperationalEventSeverity.Warning,
                    error,
                    detail: new Dictionary<string, object?>
                    {
                        ["workload"] = ticket.Kind.ToString(),
                        ["operation"] = ticket.Operation.ToString(),
                        ["stage"] = ticket.Stage.ToString(),
                        ["attempt"] = ticket.AttemptCount,
                        ["dockerSlots"] = ticket.DockerSlots,
                        ["vmSlots"] = ticket.VmSlots,
                        ["reasonCode"] = ticket.BlockedReasonCode
                    }));
                await context.SaveChangesAsync(token);
                PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "blocked");
                continue;
            }

            var nodeId = reservation.NodeId;
            ticket.TargetNodeId = nodeId;
            ticket.Status = DeploymentQueueTicketStatus.Scheduled;
            ticket.Stage = DeploymentStage.NodeExecutionWaiting;
            ticket.StageMessage = controlPlaneTicket
                ? "Waiting for platform control-plane execution capacity."
                : "Waiting for node execution capacity.";
            ticket.BlockedReasonCode = null;
            ticket.NotBeforeAt = null;
            ticket.ClaimOwner = null;
            ticket.ClaimExpiresAt = null;
            ticket.AssignedAt ??= DateTimeOffset.UtcNow;
            ticket.ErrorMessage = null;
            ticket.ErrorCategory = null;
            ticket.ErrorCode = null;
            ticket.Retryable = false;
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Runtime.SchedulingAssigned,
                OperationalEventOutcome.Succeeded,
                controlPlaneTicket
                    ? "Runtime scheduling assigned the platform control plane."
                    : "Runtime scheduling assigned a worker node.",
                workerNodeId: nodeId));
            await context.SaveChangesAsync(token);
            PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "assigned");
            await wakeup.NotifyAsync(ticket.Id, token);
            scheduled++;

            logger.SystemLog(
                $"Deployment scheduled: ticket={ticket.Id}, kind={ticket.Kind}, operation={ticket.Operation}, node={nodeId?.ToString() ?? "control-plane"}, dockerSlots={ticket.DockerSlots}, vmSlots={ticket.VmSlots}.",
                TaskStatus.Pending, LogLevel.Information);
        }

        return scheduled;
    }

    internal static async Task<bool> TryClaimAsync(AppDbContext db, Guid ticketId, DateTimeOffset now,
        CancellationToken token)
    {
        var owner = $"scheduler:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        if (db.Database.IsRelational())
            return await db.DeploymentQueueTickets
                .Where(ticket => ticket.Id == ticketId && ticket.Status == DeploymentQueueTicketStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ticket => ticket.Status, DeploymentQueueTicketStatus.Scheduling)
                    .SetProperty(ticket => ticket.Stage, DeploymentStage.AdmissionChecking)
                    .SetProperty(ticket => ticket.ClaimOwner, owner)
                    .SetProperty(ticket => ticket.ClaimExpiresAt, now.Add(ClaimTimeout)), token) == 1;

        var ticket = await db.DeploymentQueueTickets.SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket?.Status != DeploymentQueueTicketStatus.Pending)
            return false;
        ticket.Status = DeploymentQueueTicketStatus.Scheduling;
        ticket.Stage = DeploymentStage.AdmissionChecking;
        ticket.ClaimOwner = owner;
        ticket.ClaimExpiresAt = now.Add(ClaimTimeout);
        await db.SaveChangesAsync(token);
        return true;
    }

    static Task<bool> OwnsSchedulingClaimAsync(
        AppDbContext db,
        Guid ticketId,
        string claimOwner,
        CancellationToken token) =>
        db.DeploymentQueueTickets.AsNoTracking().AnyAsync(ticket =>
            ticket.Id == ticketId && ticket.Status == DeploymentQueueTicketStatus.Scheduling &&
            ticket.ClaimOwner == claimOwner, token);

    async Task RequeueStaleSchedulingClaimsAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var query = context.DeploymentQueueTickets.Where(ticket =>
            ticket.Status == DeploymentQueueTicketStatus.Scheduling && ticket.ClaimExpiresAt < now);
        if (context.Database.IsRelational())
        {
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(ticket => ticket.Status, DeploymentQueueTicketStatus.Pending)
                .SetProperty(ticket => ticket.Stage, DeploymentStage.Queued)
                .SetProperty(ticket => ticket.ClaimOwner, (string?)null)
                .SetProperty(ticket => ticket.ClaimExpiresAt, (DateTimeOffset?)null), token);
            return;
        }

        foreach (var ticket in await query.ToListAsync(token))
        {
            ticket.Status = DeploymentQueueTicketStatus.Pending;
            ticket.Stage = DeploymentStage.Queued;
            ticket.ClaimOwner = null;
            ticket.ClaimExpiresAt = null;
        }
        await context.SaveChangesAsync(token);
    }

    async Task<FleetCapacityReservationResult> ReserveCapacityAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime &&
            ticket.Operation == RuntimeOperationKind.Create &&
            ticket.TeamLabRuntimeId is { } teamLabRuntimeId)
            return await teamLabPlacement.BindAndReserveAsync(ticket.Id, teamLabRuntimeId, token);

        if (IsTeamLabControlPlaneTicket(ticket))
            return new FleetCapacityReservationResult(
                true, null, null, 0, 0, "TeamLab control operation scheduled on the platform control plane.");

        if (ticket.Operation != RuntimeOperationKind.Create)
        {
            var controlNodeId = await ResolvePreferredNodeIdAsync(ticket, token);
            return controlNodeId is { } nodeId
                ? new FleetCapacityReservationResult(true, nodeId, null, 0, 0, "Control operation scheduled.")
                : FleetCapacityReservationResult.Failed("Control operation target node is unavailable.");
        }

        var resources = await ResolveResourcesAsync(ticket, token);
        return await capacity.TryReserveAsync(ticket.Id, new FleetCapacityRequest(
            RequiredCapability(ticket), resources,
            await ResolvePreferredNodeIdAsync(ticket, token), false), token);
    }

    async Task<WorkloadResourceVector> ResolveResourcesAsync(
        DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        var specification = ticket.Kind switch
        {
            DeploymentQueueKind.GameContainer or
            DeploymentQueueKind.ChallengeTestContainer or
            DeploymentQueueKind.VirtualMachine => await context.GameChallenges.AsNoTracking()
                .Where(challenge => challenge.Id == ticket.ChallengeId &&
                                    (ticket.GameId == null || challenge.GameId == ticket.GameId))
                .Select(challenge => new RuntimeResourceSpecification(
                    challenge.CPUCount ?? 1,
                    challenge.MemoryLimit ?? 64,
                    challenge.StorageLimit ?? 256))
                .SingleOrDefaultAsync(token),
            DeploymentQueueKind.ExerciseContainer or DeploymentQueueKind.TrainingContainer =>
                await context.ExerciseChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == ticket.ChallengeId)
                    .Select(challenge => new RuntimeResourceSpecification(
                        challenge.CPUCount ?? 1,
                        challenge.MemoryLimit ?? 64,
                        challenge.StorageLimit ?? 256))
                    .SingleOrDefaultAsync(token),
            _ => null
        } ?? new RuntimeResourceSpecification(0, 0, 0);

        if (ticket.Kind == DeploymentQueueKind.VirtualMachine)
        {
            var vmCpu = specification.CpuUnits >= 1
                ? specification.CpuUnits
                : Math.Max(1, kvmOptions.Value.DefaultVmCpu);
            var vmMemory = specification.MemoryMiB >= 1024
                ? specification.MemoryMiB
                : Math.Max(1024, kvmOptions.Value.DefaultVmMemoryMb);
            return new WorkloadResourceVector(
                vmCpu * 10L,
                vmMemory,
                specification.StorageMiB,
                ticket.DockerSlots,
                ticket.VmSlots);
        }

        return new WorkloadResourceVector(
            specification.CpuUnits,
            specification.MemoryMiB,
            specification.StorageMiB,
            ticket.DockerSlots,
            ticket.VmSlots);
    }

    sealed record RuntimeResourceSpecification(int CpuUnits, int MemoryMiB, int StorageMiB);

    internal static bool IsTeamLabControlPlaneTicket(DeploymentQueueTicket ticket) =>
        ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.Operation != RuntimeOperationKind.Create;

    async Task<Guid?> ResolvePreferredNodeIdAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.TargetNodeId is { } nodeId)
            return nodeId;
        if (ticket.Kind == DeploymentQueueKind.ChallengeTestContainer &&
            ticket.GameId is { } gameId && ticket.ChallengeId is { } challengeId)
        {
            var containerNodeId = await context.GameChallenges.AsNoTracking()
                .Where(challenge => challenge.GameId == gameId && challenge.Id == challengeId)
                .Select(challenge => challenge.TestContainer == null
                    ? (Guid?)null
                    : challenge.TestContainer.NodeId)
                .SingleOrDefaultAsync(token);
            if (containerNodeId is not null)
                return containerNodeId;
            return await context.DeploymentQueueTickets.AsNoTracking()
                .Where(candidate => candidate.Kind == DeploymentQueueKind.ChallengeTestContainer &&
                                    candidate.Operation == RuntimeOperationKind.Create &&
                                    candidate.GameId == gameId && candidate.ChallengeId == challengeId &&
                                    candidate.TargetNodeId != null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .Select(candidate => candidate.TargetNodeId)
                .FirstOrDefaultAsync(token);
        }
        if (ticket.Kind != DeploymentQueueKind.TeamLabRuntime || ticket.TeamLabRuntimeId is not { } runtimeId)
            return null;

        return await context.TeamLabRuntimes.AsNoTracking()
            .Where(runtime => runtime.Id == runtimeId)
            .Select(runtime => runtime.Shards
                .Where(shard => shard.Generation == runtime.Generation && shard.Id == runtime.EntryShardId)
                .Select(shard => (Guid?)shard.WorkerNodeId)
                .FirstOrDefault())
            .SingleOrDefaultAsync(token);
    }

    async Task<bool> IsTicketStillDeployableAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Kind != DeploymentQueueKind.TeamLabRuntime ||
            ticket.Operation != RuntimeOperationKind.Create ||
            ticket.TeamLabRuntimeId is not { } runtimeId)
            return true;
        return await context.TeamLabRuntimes.AsNoTracking()
            .Where(runtime => runtime.Id == runtimeId)
            .Select(runtime => runtime.Status)
            .AnyAsync(status => status == TeamLabRuntimeStatus.Scheduled || status == TeamLabRuntimeStatus.Deploying,
                token);
    }

    static NodeCapability RequiredCapability(DeploymentQueueTicket ticket)
    {
        var capability = NodeCapability.None;
        if (ticket.DockerSlots > 0)
            capability |= NodeCapability.Docker;
        if (ticket.VmSlots > 0)
            capability |= NodeCapability.Kvm;
        return capability == NodeCapability.None && ticket.Kind == DeploymentQueueKind.VirtualMachine
            ? NodeCapability.Kvm
            : capability == NodeCapability.None ? NodeCapability.Docker : capability;
    }
}
