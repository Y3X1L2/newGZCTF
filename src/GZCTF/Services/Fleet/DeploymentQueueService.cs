using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public sealed record DeploymentQueueResult(
    Guid TicketId,
    DeploymentQueueTicketStatus Status,
    int QueuePosition,
    int PeopleAhead,
    bool ReusedExistingTicket)
{
    public static DeploymentQueueResult FromStatus(DeploymentQueueStatusModel status, bool reusedExistingTicket) =>
        new(status.TicketId, status.Status, status.QueuePosition, status.PeopleAhead, reusedExistingTicket);
}

public class DeploymentQueueService
{
    static readonly DeploymentQueueTicketStatus[] ActiveStatuses =
    [
        DeploymentQueueTicketStatus.Pending,
        DeploymentQueueTicketStatus.Assigned,
        DeploymentQueueTicketStatus.Creating
    ];

    readonly AppDbContext _context;
    readonly FleetCapacityReservationService? _capacity;
    readonly ILogger<DeploymentQueueService> _logger;

    public DeploymentQueueService(AppDbContext context, ILogger<DeploymentQueueService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public DeploymentQueueService(AppDbContext context, FleetCapacityReservationService capacity,
        ILogger<DeploymentQueueService> logger)
    {
        _context = context;
        _capacity = capacity;
        _logger = logger;
    }

    public async Task<DeploymentQueueResult> EnqueueAsync(DeploymentQueueRequest request, CancellationToken token)
    {
        var identity = DeploymentQueueTicket.BuildActiveIdentity(request);
        var existing = await _context.DeploymentQueueTickets
            .Include(t => t.TargetNode)
            .FirstOrDefaultAsync(t => t.ActiveIdentity == identity && ActiveStatuses.Contains(t.Status), token);

        if (existing is not null)
        {
            var existingStatus = await GetStatusAsync(existing.Id, token)
                ?? DeploymentQueueStatusModel.FromTicket(existing, queuePosition: 0);
            return DeploymentQueueResult.FromStatus(existingStatus, reusedExistingTicket: true);
        }

        var ticket = DeploymentQueueTicket.Create(request);
        _context.DeploymentQueueTickets.Add(ticket);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation("Deployment queue ticket {TicketId} created for {Kind}", ticket.Id, ticket.Kind);

        var status = await GetStatusAsync(ticket.Id, token)
            ?? DeploymentQueueStatusModel.FromTicket(ticket, queuePosition: 0);
        return DeploymentQueueResult.FromStatus(status, reusedExistingTicket: false);
    }

    public async Task<DeploymentQueueStatusModel?> GetStatusAsync(Guid ticketId, CancellationToken token)
    {
        var ticket = await _context.DeploymentQueueTickets
            .Include(t => t.TargetNode)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId, token);

        if (ticket is null)
            return null;

        var position = await GetQueuePositionAsync(ticket, token);
        return DeploymentQueueStatusModel.FromTicket(ticket, position);
    }

    public async Task CancelAsync(Guid ticketId, string reason, CancellationToken token)
    {
        var ticket = await _context.DeploymentQueueTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId, token);
        if (ticket is null || !ActiveStatuses.Contains(ticket.Status))
            return;

        var shouldReleaseCapacity =
            (ticket.Status is DeploymentQueueTicketStatus.Assigned or DeploymentQueueTicketStatus.Creating) &&
            ticket.TargetNodeId is not null;
        var nodeId = ticket.TargetNodeId;
        var dockerSlots = ticket.DockerSlots;
        var vmSlots = ticket.VmSlots;

        ticket.Status = DeploymentQueueTicketStatus.Cancelled;
        ticket.ErrorMessage = TrimError(reason);
        ticket.CompletedAt = DateTimeOffset.UtcNow;

        if (ticket.DeploymentTargetId is { } targetId)
        {
            var target = await _context.DeploymentTargets.FirstOrDefaultAsync(t => t.Id == targetId, token);
            if (target is not null &&
                (target.Status == TargetStatus.Pending ||
                 target.Status == TargetStatus.Assigned ||
                 target.Status == TargetStatus.Creating))
            {
                target.Status = TargetStatus.Cancelled;
                target.ErrorMessage = ticket.ErrorMessage;
                target.CompletedAt = ticket.CompletedAt;
            }
        }

        if (shouldReleaseCapacity && _capacity is not null && nodeId is { } reservedNodeId)
            await _capacity.ReleaseAsync(reservedNodeId, dockerSlots, vmSlots, token);
        else
            await _context.SaveChangesAsync(token);
    }

    public async Task<int> RecoverStaleCreatingTicketsAsync(TimeSpan staleAfter, CancellationToken token)
    {
        var cutoff = DateTimeOffset.UtcNow - staleAfter;
        var tickets = await _context.DeploymentQueueTickets
            .Include(t => t.DeploymentTarget)
            .Where(t => t.Status == DeploymentQueueTicketStatus.Creating)
            .Where(t => (t.StartedAt ?? t.AssignedAt ?? t.CreatedAt) < cutoff)
            .ToListAsync(token);

        foreach (var ticket in tickets)
        {
            var nodeId = ticket.TargetNodeId;
            var dockerSlots = ticket.DockerSlots;
            var vmSlots = ticket.VmSlots;

            ticket.Status = DeploymentQueueTicketStatus.Failed;
            ticket.ErrorMessage = "Deployment queue ticket recovered after stale Creating state.";
            ticket.CompletedAt = DateTimeOffset.UtcNow;

            if (ticket.DeploymentTarget is not null)
            {
                ticket.DeploymentTarget.Status = TargetStatus.Failed;
                ticket.DeploymentTarget.ErrorMessage = ticket.ErrorMessage;
                ticket.DeploymentTarget.CompletedAt = ticket.CompletedAt;
            }

            if (_capacity is not null && nodeId is { } reservedNodeId)
                await _capacity.ReleaseAsync(reservedNodeId, dockerSlots, vmSlots, token);
        }

        if (_capacity is null)
            await _context.SaveChangesAsync(token);

        return tickets.Count;
    }

    async Task<int> GetQueuePositionAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Status != DeploymentQueueTicketStatus.Pending)
            return 0;

        return await _context.DeploymentQueueTickets
            .AsNoTracking()
            .Where(t => t.Kind == ticket.Kind && t.Status == DeploymentQueueTicketStatus.Pending)
            .Where(t => t.CreatedAt < ticket.CreatedAt ||
                        (t.CreatedAt == ticket.CreatedAt && string.Compare(t.Id.ToString(), ticket.Id.ToString(), StringComparison.Ordinal) <= 0))
            .CountAsync(token);
    }

    static string TrimError(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "Deployment queue ticket was cancelled."
            : reason.Length <= 1024 ? reason : reason[..1024];
}
