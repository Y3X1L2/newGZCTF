using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Application;

public sealed class RuntimeSchedulingService(
    AppDbContext context,
    FleetCapacityReservationService capacity,
    RuntimeQueueSelector selector,
    TeamLabPhysicalPlacementService teamLabPlacement,
    IDeploymentQueueWakeup wakeup,
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
            ticket.Stage = DeploymentStage.AdmissionChecking;

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
                await context.SaveChangesAsync(token);
                continue;
            }

            var reservation = await ReserveCapacityAsync(ticket, token);
            if (!await OwnsSchedulingClaimAsync(context, ticket.Id, claimOwner, token))
            {
                context.ChangeTracker.Clear();
                continue;
            }
            if (!reservation.Success || reservation.NodeId is not { } nodeId)
            {
                ticket.Status = DeploymentQueueTicketStatus.Pending;
                ticket.Stage = DeploymentStage.CapacityWaiting;
                ticket.BlockedReasonCode = "node_capacity_exhausted";
                ticket.StageMessage = reservation.Message;
                ticket.AttemptCount++;
                ticket.NotBeforeAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, 1 << Math.Min(5, ticket.AttemptCount)));
                ticket.ClaimOwner = null;
                ticket.ClaimExpiresAt = null;
                await context.SaveChangesAsync(token);
                continue;
            }

            ticket.TargetNodeId = nodeId;
            ticket.Status = DeploymentQueueTicketStatus.Scheduled;
            ticket.Stage = DeploymentStage.NodeExecutionWaiting;
            ticket.StageMessage = "Waiting for node execution capacity.";
            ticket.BlockedReasonCode = null;
            ticket.NotBeforeAt = null;
            ticket.ClaimOwner = null;
            ticket.ClaimExpiresAt = null;
            ticket.AssignedAt ??= DateTimeOffset.UtcNow;
            ticket.ErrorMessage = null;
            await context.SaveChangesAsync(token);
            await wakeup.NotifyAsync(ticket.Id, token);
            scheduled++;

            logger.SystemLog(
                $"Deployment scheduled: ticket={ticket.Id}, kind={ticket.Kind}, operation={ticket.Operation}, node={nodeId}, dockerSlots={ticket.DockerSlots}, vmSlots={ticket.VmSlots}.",
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

        if (ticket.Operation != RuntimeOperationKind.Create)
        {
            var controlNodeId = await ResolvePreferredNodeIdAsync(ticket, token);
            return controlNodeId is { } nodeId
                ? new FleetCapacityReservationResult(true, nodeId, null, 0, 0, "Control operation scheduled.")
                : FleetCapacityReservationResult.Failed("Control operation target node is unavailable.");
        }

        return await capacity.TryReserveAsync(ticket.Id, new FleetCapacityRequest(
            RequiredCapability(ticket), ticket.DockerSlots, ticket.VmSlots,
            await ResolvePreferredNodeIdAsync(ticket, token), false), token);
    }

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
