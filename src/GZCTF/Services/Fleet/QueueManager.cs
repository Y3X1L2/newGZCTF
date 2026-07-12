using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class QueueManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NodeExecutionGate _executionGate;
    private readonly ILogger<QueueManager> _logger;
    private const int DefaultBatchSize = 20;
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(2);

    public QueueManager(IServiceScopeFactory scopeFactory,
        NodeExecutionGate executionGate, ILogger<QueueManager> logger)
    {
        _scopeFactory = scopeFactory;
        _executionGate = executionGate;
        _logger = logger;
    }

    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();

        var staleClaimCutoff = DateTimeOffset.UtcNow - ClaimTimeout;
        await RequeueStaleClaimsAsync(context, staleClaimCutoff, token);

        var pendingTicketIds = await context.DeploymentQueueTickets
            .AsNoTracking()
            .Where(t => t.Status == DeploymentQueueTicketStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .Take(DefaultBatchSize)
            .Select(t => t.Id)
            .ToListAsync(token);

        var executableTickets = new List<ReservedQueueTicket>();
        foreach (var ticketId in pendingTicketIds)
        {
            if (token.IsCancellationRequested)
                break;

            var claimedAt = DateTimeOffset.UtcNow;
            if (!await TryClaimTicketAsync(context, ticketId, claimedAt, token))
                continue;

            var ticket = await context.DeploymentQueueTickets
                .Include(t => t.DeploymentTarget)
                .SingleAsync(t => t.Id == ticketId, token);
            if (!await IsTicketStillDeployableAsync(context, ticket, token))
            {
                ticket.Status = DeploymentQueueTicketStatus.Cancelled;
                ticket.CompletedAt = DateTimeOffset.UtcNow;
                ticket.ErrorMessage = "Deployment queue ticket is not deployable anymore.";
                await context.SaveChangesAsync(token);
                continue;
            }

            var reservation = await ReserveTicketCapacityAsync(context, capacity, ticket, token);

            if (!reservation.Success || reservation.NodeId is not { } nodeId)
            {
                _logger.LogDebug("Still no capacity for deployment queue ticket {TicketId}: {Message}",
                    ticket.Id, reservation.Message);
                ticket.Status = DeploymentQueueTicketStatus.Pending;
                ticket.AssignedAt = null;
                await context.SaveChangesAsync(token);
                continue;
            }

            ticket.TargetNodeId = nodeId;
            ticket.Status = DeploymentQueueTicketStatus.Creating;
            ticket.AssignedAt ??= DateTimeOffset.UtcNow;
            ticket.StartedAt ??= DateTimeOffset.UtcNow;
            ticket.ErrorMessage = null;
            if (ticket.DeploymentTarget is not null)
            {
                ticket.DeploymentTarget.TargetNodeId = nodeId;
                ticket.DeploymentTarget.Status = TargetStatus.Creating;
                ticket.DeploymentTarget.ErrorMessage = null;
            }

            await context.SaveChangesAsync(token);
            _logger.SystemLog(
                $"Deployment queue ticket assigned: ticket={ticket.Id}, kind={ticket.Kind}, node={nodeId}, dockerSlots={ticket.DockerSlots}, vmSlots={ticket.VmSlots}.",
                TaskStatus.Pending, LogLevel.Information);
            executableTickets.Add(new ReservedQueueTicket(ticket.Id, nodeId, ticket.DockerSlots, ticket.VmSlots,
                ticket.Kind == DeploymentQueueKind.TeamLabRuntime));
        }

        if (executableTickets.Count == 0)
            return 0;

        var results = await Task.WhenAll(executableTickets.Select(ticket => ExecuteReservedTicketAsync(ticket, token)));
        return results.Count(processed => processed);
    }

    internal static async Task<bool> TryClaimTicketAsync(
        AppDbContext context,
        Guid ticketId,
        DateTimeOffset claimedAt,
        CancellationToken token)
    {
        if (context.Database.IsRelational())
            return await context.DeploymentQueueTickets
                .Where(t => t.Id == ticketId && t.Status == DeploymentQueueTicketStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, DeploymentQueueTicketStatus.Assigned)
                    .SetProperty(t => t.AssignedAt, claimedAt), token) == 1;

        var ticket = await context.DeploymentQueueTickets
            .SingleOrDefaultAsync(t => t.Id == ticketId, token);
        if (ticket?.Status != DeploymentQueueTicketStatus.Pending)
            return false;
        ticket.Status = DeploymentQueueTicketStatus.Assigned;
        ticket.AssignedAt = claimedAt;
        await context.SaveChangesAsync(token);
        return true;
    }

    private static async Task RequeueStaleClaimsAsync(
        AppDbContext context,
        DateTimeOffset staleClaimCutoff,
        CancellationToken token)
    {
        var query = context.DeploymentQueueTickets
            .Where(t => t.Status == DeploymentQueueTicketStatus.Assigned &&
                        t.TargetNodeId == null && t.AssignedAt < staleClaimCutoff);
        if (context.Database.IsRelational())
        {
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Status, DeploymentQueueTicketStatus.Pending)
                .SetProperty(t => t.AssignedAt, (DateTimeOffset?)null), token);
            return;
        }

        var staleTickets = await query.ToListAsync(token);
        foreach (var ticket in staleTickets)
        {
            ticket.Status = DeploymentQueueTicketStatus.Pending;
            ticket.AssignedAt = null;
        }

        if (staleTickets.Count > 0)
            await context.SaveChangesAsync(token);
    }

    async Task<bool> ExecuteReservedTicketAsync(ReservedQueueTicket reserved, CancellationToken token)
    {
        try
        {
            if (reserved.IsTeamLabRuntime)
            {
                await ExecuteReservedTicketBodyAsync(reserved, token);
                return true;
            }

            await _executionGate.RunAsync(reserved.NodeId, async executionToken =>
            {
                await ExecuteReservedTicketBodyAsync(reserved, executionToken);
            }, token);

            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment queue ticket {TicketId} failed during execution.", reserved.TicketId);
            await MarkExecutionExceptionAsync(reserved, ex, token);
            return true;
        }
    }

    async Task ExecuteReservedTicketBodyAsync(ReservedQueueTicket reserved, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        var executor = scope.ServiceProvider.GetRequiredService<DeploymentExecutionService>();
        var ticket = await context.DeploymentQueueTickets
            .Include(t => t.DeploymentTarget)
            .FirstOrDefaultAsync(t => t.Id == reserved.TicketId, token);

        if (ticket is null)
        {
            await ReleaseReservedTicketCapacityAsync(context, capacity, reserved, token);
            _logger.LogWarning("Reserved deployment queue ticket {TicketId} disappeared before execution.",
                reserved.TicketId);
            return;
        }

        if (ticket.Status != DeploymentQueueTicketStatus.Creating)
            return;

        _logger.SystemLog(
            $"Deployment queue ticket started: ticket={ticket.Id}, kind={ticket.Kind}, node={reserved.NodeId}, dockerSlots={reserved.DockerSlots}, vmSlots={reserved.VmSlots}.",
            TaskStatus.Pending, LogLevel.Information);

        if (!await IsTicketStillDeployableAsync(context, ticket, token))
        {
            await ReleaseReservedTicketCapacityAsync(context, capacity, reserved, token);
            ticket.Status = DeploymentQueueTicketStatus.Cancelled;
            ticket.CompletedAt = DateTimeOffset.UtcNow;
            ticket.ErrorMessage = "Deployment queue ticket is not deployable anymore.";
            await context.SaveChangesAsync(token);
            _logger.SystemLog(
                $"Deployment queue ticket cancelled before execution: ticket={ticket.Id}, kind={ticket.Kind}.",
                TaskStatus.Exit, LogLevel.Warning);
            return;
        }

        var execution = await executor.ExecuteAsync(ticket, token);
        await context.Entry(ticket).ReloadAsync(token);
        if (ticket.Status == DeploymentQueueTicketStatus.Cancelled)
        {
            _logger.SystemLog(
                $"Deployment queue ticket completed after cancellation and was left cancelled: ticket={ticket.Id}, kind={ticket.Kind}.",
                TaskStatus.Exit,
                LogLevel.Warning);
            return;
        }

        if (execution.Success)
        {
            await CompleteTicketAsync(context, capacity, ticket, reserved, token);
            _logger.SystemLog(
                $"Deployment queue ticket completed: ticket={ticket.Id}, kind={ticket.Kind}, node={reserved.NodeId}.",
                TaskStatus.Success, LogLevel.Information);
        }
        else
        {
            await FailTicketAsync(context, capacity, ticket, reserved, execution.ErrorMessage, token);
            _logger.SystemLog(
                $"Deployment queue ticket failed: ticket={ticket.Id}, kind={ticket.Kind}, node={reserved.NodeId}, error={ticket.ErrorMessage}.",
                TaskStatus.Failed, LogLevel.Warning);
        }

        await context.SaveChangesAsync(token);
    }

    static async Task CompleteTicketAsync(AppDbContext context, FleetCapacityReservationService capacity,
        DeploymentQueueTicket ticket, ReservedQueueTicket reserved, CancellationToken token)
    {
        await ConfirmReservedTicketCapacityAsync(context, capacity, ticket, reserved, token);
        ticket.Status = DeploymentQueueTicketStatus.Completed;
        ticket.CompletedAt = DateTimeOffset.UtcNow;
        ticket.ErrorMessage = null;
        if (ticket.DeploymentTarget is null)
            return;

        ticket.DeploymentTarget.Status = TargetStatus.Completed;
        ticket.DeploymentTarget.CompletedAt = ticket.CompletedAt;
        ticket.DeploymentTarget.ErrorMessage = null;
    }

    static async Task FailTicketAsync(AppDbContext context, FleetCapacityReservationService capacity,
        DeploymentQueueTicket ticket, ReservedQueueTicket reserved, string? errorMessage, CancellationToken token)
    {
        await ReleaseReservedTicketCapacityAsync(context, capacity, reserved, token);
        ticket.Status = DeploymentQueueTicketStatus.Failed;
        ticket.CompletedAt = DateTimeOffset.UtcNow;
        ticket.ErrorMessage = TrimError(errorMessage);
        if (ticket.DeploymentTarget is null)
            return;

        ticket.DeploymentTarget.Status = TargetStatus.Failed;
        ticket.DeploymentTarget.CompletedAt = ticket.CompletedAt;
        ticket.DeploymentTarget.ErrorMessage = ticket.ErrorMessage;
    }

    async Task MarkExecutionExceptionAsync(ReservedQueueTicket reserved, Exception ex, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        var ticket = await context.DeploymentQueueTickets
            .Include(t => t.DeploymentTarget)
            .FirstOrDefaultAsync(t => t.Id == reserved.TicketId, token);
        if (ticket is null)
        {
            await ReleaseReservedTicketCapacityAsync(context, capacity, reserved, token);
            return;
        }

        if (ticket.Status != DeploymentQueueTicketStatus.Creating)
            return;

        await FailTicketAsync(context, capacity, ticket, reserved, ex.Message, token);
        await context.SaveChangesAsync(token);
        _logger.SystemLog(
            $"Deployment queue ticket failed with exception: ticket={ticket.Id}, kind={ticket.Kind}, node={reserved.NodeId}, error={ticket.ErrorMessage}.",
            TaskStatus.Failed, LogLevel.Warning);
    }

    static async Task<FleetCapacityReservationResult> ReserveTicketCapacityAsync(AppDbContext context,
        FleetCapacityReservationService capacity, DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            var shardSlots = await TeamLabCapacityFacts.LoadAsync(context, runtimeId, token);
            if (shardSlots.Length == 0)
                return FleetCapacityReservationResult.Failed("TeamLab runtime has no planned shard capacity.");

            var reservation = await capacity.TryReserveBatchAsync(
                shardSlots.Select(slot => new FleetCapacityBatchItem(slot.WorkerNodeId, slot.DockerSlots,
                    slot.VmSlots)).ToArray(),
                requireTeamLab: true,
                token);
            if (!reservation.Success)
                return FleetCapacityReservationResult.Failed(reservation.Message);

            var primaryNodeId = await ResolvePreferredNodeIdAsync(context, ticket, token);
            return reservation.Reservations.FirstOrDefault(r => r.NodeId == primaryNodeId) ??
                   reservation.Reservations.First();
        }

        return await capacity.TryReserveAsync(new FleetCapacityRequest(
            GetRequiredCapability(ticket),
            ticket.DockerSlots,
            ticket.VmSlots,
            PreferredNodeId: await ResolvePreferredNodeIdAsync(context, ticket, token),
            RequireTeamLab: false), token);
    }

    static async Task ConfirmReservedTicketCapacityAsync(AppDbContext context,
        FleetCapacityReservationService capacity, DeploymentQueueTicket ticket, ReservedQueueTicket reserved,
        CancellationToken token)
    {
        if (reserved.IsTeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            var shardSlots = await TeamLabCapacityFacts.LoadAsync(context, runtimeId, token);
            foreach (var slot in shardSlots)
                await capacity.ConfirmAsync(slot.WorkerNodeId, slot.DockerSlots, slot.VmSlots, token);
            return;
        }

        await capacity.ConfirmAsync(reserved.NodeId, reserved.DockerSlots, reserved.VmSlots, token);
    }

    static async Task ReleaseReservedTicketCapacityAsync(AppDbContext context, FleetCapacityReservationService capacity,
        ReservedQueueTicket reserved, CancellationToken token)
    {
        if (reserved.IsTeamLabRuntime)
        {
            var ticket = await context.DeploymentQueueTickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == reserved.TicketId, token);
            if (ticket?.TeamLabRuntimeId is { } runtimeId)
            {
                var shardSlots = await TeamLabCapacityFacts.LoadAsync(context, runtimeId, token);
                foreach (var slot in shardSlots)
                    await capacity.ReleaseAsync(slot.WorkerNodeId, slot.DockerSlots, slot.VmSlots, token);
                return;
            }
        }

        await capacity.ReleaseAsync(reserved.NodeId, reserved.DockerSlots, reserved.VmSlots, token);
    }

    static NodeCapability GetRequiredCapability(DeploymentQueueTicket ticket)
    {
        var capability = NodeCapability.None;
        if (ticket.DockerSlots > 0)
            capability |= NodeCapability.Docker;
        if (ticket.VmSlots > 0)
            capability |= NodeCapability.Kvm;

        return capability == NodeCapability.None
            ? ticket.Kind == DeploymentQueueKind.Vm ? NodeCapability.Kvm : NodeCapability.Docker
            : capability;
    }

    static async Task<bool> IsTicketStillDeployableAsync(AppDbContext context, DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.Kind != DeploymentQueueKind.TeamLabRuntime || ticket.TeamLabRuntimeId is not { } runtimeId)
            return true;

        var status = await context.TeamLabRuntimes
            .AsNoTracking()
            .Where(runtime => runtime.Id == runtimeId)
            .Select(runtime => (TeamLabRuntimeStatus?)runtime.Status)
            .SingleOrDefaultAsync(token);

        return status is TeamLabRuntimeStatus.Scheduled or TeamLabRuntimeStatus.Deploying;
    }

    static async Task<Guid?> ResolvePreferredNodeIdAsync(AppDbContext context, DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.TargetNodeId is { } targetNodeId)
            return targetNodeId;

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

    static string TrimError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Deployment queue execution failed.";

        return message.Length <= 1024 ? message : message[..1024];
    }

    sealed record ReservedQueueTicket(Guid TicketId, Guid NodeId, int DockerSlots, int VmSlots,
        bool IsTeamLabRuntime = false);
}
