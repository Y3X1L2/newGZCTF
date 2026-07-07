using GZCTF.Models.Data;
using GZCTF.Services.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class QueueManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly NodeExecutionGate _executionGate;
    private readonly ILogger<QueueManager> _logger;
    private const int DefaultBatchSize = 20;

    public QueueManager(IServiceScopeFactory scopeFactory, IDistributedLockService lockService,
        NodeExecutionGate executionGate, ILogger<QueueManager> logger)
    {
        _scopeFactory = scopeFactory;
        _lockService = lockService;
        _executionGate = executionGate;
        _logger = logger;
    }

    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();

        var pendingTickets = await context.DeploymentQueueTickets
            .Include(t => t.DeploymentTarget)
            .Where(t => t.Status == DeploymentQueueTicketStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .Take(DefaultBatchSize)
            .ToListAsync(token);

        var executableTickets = new List<ReservedQueueTicket>();
        foreach (var ticket in pendingTickets)
        {
            if (token.IsCancellationRequested)
                break;

            var reservation = await capacity.TryReserveAsync(new FleetCapacityRequest(
                GetRequiredCapability(ticket),
                ticket.DockerSlots,
                ticket.VmSlots,
                PreferredNodeId: await ResolvePreferredNodeIdAsync(context, ticket, token),
                RequireTeamLab: ticket.Kind == DeploymentQueueKind.TeamLabRuntime), token);

            if (!reservation.Success || reservation.NodeId is not { } nodeId)
            {
                _logger.LogDebug("Still no capacity for deployment queue ticket {TicketId}: {Message}",
                    ticket.Id, reservation.Message);
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
            executableTickets.Add(new ReservedQueueTicket(ticket.Id, nodeId, ticket.DockerSlots, ticket.VmSlots));
        }

        if (executableTickets.Count == 0)
            return 0;

        var results = await Task.WhenAll(executableTickets.Select(ticket => ExecuteReservedTicketAsync(ticket, token)));
        return results.Count(processed => processed);
    }

    async Task<bool> ExecuteReservedTicketAsync(ReservedQueueTicket reserved, CancellationToken token)
    {
        try
        {
            await _executionGate.RunAsync(reserved.NodeId, async executionToken =>
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
                var executor = scope.ServiceProvider.GetRequiredService<DeploymentExecutionService>();
                var ticket = await context.DeploymentQueueTickets
                    .Include(t => t.DeploymentTarget)
                    .FirstOrDefaultAsync(t => t.Id == reserved.TicketId, executionToken);

                if (ticket is null)
                {
                    await capacity.ReleaseAsync(reserved.NodeId, reserved.DockerSlots, reserved.VmSlots, executionToken);
                    _logger.LogWarning("Reserved deployment queue ticket {TicketId} disappeared before execution.",
                        reserved.TicketId);
                    return;
                }

                if (ticket.Status != DeploymentQueueTicketStatus.Creating)
                    return;

                var execution = await executor.ExecuteAsync(ticket, executionToken);
                if (execution.Success)
                    await CompleteTicketAsync(capacity, ticket, reserved.NodeId, executionToken);
                else
                    await FailTicketAsync(context, capacity, ticket, reserved.NodeId, execution.ErrorMessage,
                        executionToken);

                await context.SaveChangesAsync(executionToken);
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

    static async Task CompleteTicketAsync(FleetCapacityReservationService capacity, DeploymentQueueTicket ticket,
        Guid nodeId, CancellationToken token)
    {
        await capacity.ConfirmAsync(nodeId, ticket.DockerSlots, ticket.VmSlots, token);
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
        DeploymentQueueTicket ticket, Guid nodeId, string? errorMessage, CancellationToken token)
    {
        await capacity.ReleaseAsync(nodeId, ticket.DockerSlots, ticket.VmSlots, token);
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
            await capacity.ReleaseAsync(reserved.NodeId, reserved.DockerSlots, reserved.VmSlots, token);
            return;
        }

        if (ticket.Status != DeploymentQueueTicketStatus.Creating)
            return;

        await FailTicketAsync(context, capacity, ticket, reserved.NodeId, ex.Message, token);
        await context.SaveChangesAsync(token);
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

    static async Task<Guid?> ResolvePreferredNodeIdAsync(AppDbContext context, DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.TargetNodeId is { } targetNodeId)
            return targetNodeId;

        if (ticket.Kind != DeploymentQueueKind.TeamLabRuntime || ticket.TeamLabRuntimeId is not { } runtimeId)
            return null;

        return await context.TeamLabRuntimes
            .AsNoTracking()
            .Where(runtime => runtime.Id == runtimeId)
            .Select(runtime => runtime.WorkerNodeId)
            .SingleOrDefaultAsync(token);
    }

    static string TrimError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Deployment queue execution failed.";

        return message.Length <= 1024 ? message : message[..1024];
    }

    sealed record ReservedQueueTicket(Guid TicketId, Guid NodeId, int DockerSlots, int VmSlots);
}
