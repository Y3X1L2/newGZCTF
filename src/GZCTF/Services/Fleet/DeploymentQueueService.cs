using System.Diagnostics;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
        DeploymentQueueTicketStatus.Scheduling,
        DeploymentQueueTicketStatus.Scheduled,
        DeploymentQueueTicketStatus.Running
    ];

    readonly AppDbContext _context;
    readonly FleetCapacityReservationService? _capacity;
    readonly ILogger<DeploymentQueueService> _logger;
    readonly IDeploymentQueueWakeup _wakeup;
    readonly RuntimeAdmissionPolicy? _admission;
    readonly IOperationalEventWriter _events;
    readonly OperationalCorrelation _correlation;

    public DeploymentQueueService(AppDbContext context, ILogger<DeploymentQueueService> logger)
    {
        _context = context;
        _logger = logger;
        _wakeup = new PollingDeploymentQueueWakeup();
        _events = DefaultEvents(context);
        _correlation = new OperationalCorrelation();
    }

    public DeploymentQueueService(AppDbContext context, FleetCapacityReservationService capacity,
        ILogger<DeploymentQueueService> logger)
    {
        _context = context;
        _capacity = capacity;
        _logger = logger;
        _wakeup = new PollingDeploymentQueueWakeup();
        _events = DefaultEvents(context);
        _correlation = new OperationalCorrelation();
    }

    public DeploymentQueueService(AppDbContext context, FleetCapacityReservationService capacity,
        IDeploymentQueueWakeup wakeup, ILogger<DeploymentQueueService> logger)
    {
        _context = context;
        _capacity = capacity;
        _wakeup = wakeup;
        _logger = logger;
        _events = DefaultEvents(context);
        _correlation = new OperationalCorrelation();
    }

    public DeploymentQueueService(AppDbContext context, FleetCapacityReservationService capacity,
        RuntimeAdmissionPolicy admission, IDeploymentQueueWakeup wakeup,
        ILogger<DeploymentQueueService> logger)
    {
        _context = context;
        _capacity = capacity;
        _admission = admission;
        _wakeup = wakeup;
        _events = DefaultEvents(context);
        _correlation = new OperationalCorrelation();
        _logger = logger;
    }

    public DeploymentQueueService(AppDbContext context, FleetCapacityReservationService capacity,
        RuntimeAdmissionPolicy admission, IDeploymentQueueWakeup wakeup,
        IOperationalEventWriter events, OperationalCorrelation correlation,
        ILogger<DeploymentQueueService> logger)
    {
        _context = context;
        _capacity = capacity;
        _admission = admission;
        _wakeup = wakeup;
        _events = events;
        _correlation = correlation;
        _logger = logger;
    }

    public async Task<DeploymentQueueResult> EnqueueAsync(DeploymentQueueRequest request, CancellationToken token)
    {
        using var activity = PlatformTelemetry.RuntimeActivitySource.StartActivity("runtime.enqueue", ActivityKind.Producer);
        activity?.SetTag("runtime.workload", request.Kind.ToString());
        activity?.SetTag("runtime.operation", request.Operation.ToString());
        var identity = DeploymentQueueTicket.BuildActiveIdentity(request);
        var subjectKey = DeploymentQueueTicket.BuildSubjectConcurrencyKey(request);
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(token)
            : null;
        if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({subjectKey}, 0))", token);
            if (request.Operation == RuntimeOperationKind.Create)
            {
                var ownerAdmissionKey = $"runtime-owner-admission:{RuntimeQueueSelector.OwnerKey(request.OwnerTeamId, request.OwnerUserId)}";
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({ownerAdmissionKey}, 0))", token);
            }
        }
        var existing = await _context.DeploymentQueueTickets
            .Include(t => t.TargetNode)
            .FirstOrDefaultAsync(t => t.ActiveIdentity == identity && ActiveStatuses.Contains(t.Status), token);

        if (existing is not null)
        {
            using var correlationScope = _correlation.Begin(existing.Id);
            _events.Append(RuntimeOperationalEvents.Ticket(
                existing,
                OperationalEventCodes.Runtime.TicketDuplicate,
                OperationalEventOutcome.Observed,
                "An active deployment ticket was reused."));
            await _context.SaveChangesAsync(token);
            var existingStatus = await GetStatusAsync(existing.Id, token)
                ?? DeploymentQueueStatusModel.FromTicket(existing, queuePosition: 0);
            _logger.SystemLog(
                $"Deployment queue ticket reused: ticket={existing.Id}, kind={existing.Kind}, status={existing.Status}, ownerTeam={existing.OwnerTeamId}, ownerUser={existing.OwnerUserId}, game={existing.GameId}, challenge={existing.ChallengeId}.",
                TaskStatus.Pending, LogLevel.Information);
            if (transaction is not null)
                await transaction.CommitAsync(token);
            return DeploymentQueueResult.FromStatus(existingStatus, reusedExistingTicket: true);
        }

        var subjectTickets = await _context.DeploymentQueueTickets
            .Include(t => t.TargetNode)
            .Where(t => t.SubjectConcurrencyKey == subjectKey && ActiveStatuses.Contains(t.Status))
            .OrderBy(t => t.Status == DeploymentQueueTicketStatus.Running ? 0 : 1)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(token);
        if (subjectTickets.Count > 0)
        {
            if (request.Operation == RuntimeOperationKind.Create)
            {
                var subjectTicket = subjectTickets[0];
                using var correlationScope = _correlation.Begin(subjectTicket.Id);
                _events.Append(RuntimeOperationalEvents.Ticket(
                    subjectTicket,
                    OperationalEventCodes.Runtime.TicketDuplicate,
                    OperationalEventOutcome.Observed,
                    "A subject-level active deployment ticket was reused."));
                await _context.SaveChangesAsync(token);
                var subjectStatus = await GetStatusAsync(subjectTicket.Id, token)
                    ?? DeploymentQueueStatusModel.FromTicket(subjectTicket, queuePosition: 0);
                if (transaction is not null)
                    await transaction.CommitAsync(token);
                return DeploymentQueueResult.FromStatus(subjectStatus, reusedExistingTicket: true);
            }

            foreach (var subjectTicket in subjectTickets.Where(ticket =>
                         ticket.Status != DeploymentQueueTicketStatus.Running))
                await CancelAsync(subjectTicket.Id,
                    $"Superseded by {request.Operation} control operation.", token);
        }

        if (_admission is not null)
        {
            try
            {
                await _admission.EnsureQueueCapacityAsync(request, token);
            }
            catch (Exception exception)
            {
                var correlationId = _correlation.Current ?? Guid.CreateVersion7();
                await _events.AppendAndSaveAsync(new OperationalEventDraft(
                    OperationalEventCodes.Runtime.AdmissionBlocked,
                    OperationalEventOutcome.Failed,
                    "Runtime admission rejected the deployment request.",
                    OperationalEventSeverity.Warning,
                    correlationId,
                    OperationalErrorCategory.Capacity,
                    OperationalErrorCodes.RuntimeCapacityExhausted,
                    true,
                    new Dictionary<string, object?>
                    {
                        ["workload"] = request.Kind.ToString(),
                        ["operation"] = request.Operation.ToString(),
                        ["dockerSlots"] = request.DockerSlots,
                        ["vmSlots"] = request.VmSlots
                    },
                    OwnerUserId: request.OwnerUserId,
                    OwnerTeamId: request.OwnerTeamId,
                    GameId: request.GameId,
                    ChallengeId: request.ChallengeId,
                    TeamLabRuntimeId: request.TeamLabRuntimeId,
                    VmInstanceId: request.VmInstanceId,
                    SubjectType: request.SubjectType ?? request.Kind.ToString(),
                    SubjectId: request.SubjectPublicId,
                    SubjectDisplayName: request.SubjectDisplayName,
                    ResourceType: "deployment-request",
                    ResourceId: identity,
                    ResourceDisplayName: request.ResourceDisplayName), token);
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                throw;
            }
        }

        var ticket = DeploymentQueueTicket.Create(request);
        using var ticketCorrelationScope = _correlation.Begin(ticket.Id);
        _context.DeploymentQueueTickets.Add(ticket);
        _events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            OperationalEventCodes.Runtime.AdmissionAccepted,
            OperationalEventOutcome.Succeeded,
            "Runtime admission accepted the deployment request."));
        _events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            OperationalEventCodes.Runtime.TicketEnqueued,
            OperationalEventOutcome.Pending,
            "Deployment ticket entered the runtime queue."));
        await _context.SaveChangesAsync(token);
        PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "enqueued");
        activity?.SetTag("gzctf.deployment_ticket_id", ticket.Id.ToString());

        _logger.LogInformation("Deployment queue ticket {TicketId} created for {Kind}", ticket.Id, ticket.Kind);
        _logger.SystemLog(
            $"Deployment queue ticket created: ticket={ticket.Id}, kind={ticket.Kind}, ownerTeam={ticket.OwnerTeamId}, ownerUser={ticket.OwnerUserId}, game={ticket.GameId}, challenge={ticket.ChallengeId}, dockerSlots={ticket.DockerSlots}, vmSlots={ticket.VmSlots}.",
            TaskStatus.Pending, LogLevel.Information);

        var status = await GetStatusAsync(ticket.Id, token)
            ?? DeploymentQueueStatusModel.FromTicket(ticket, queuePosition: 0);
        if (transaction is not null)
            await transaction.CommitAsync(token);
        await _wakeup.NotifyAsync(ticket.Id, token);
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
        if (ticket.Status == DeploymentQueueTicketStatus.Running)
        {
            _logger.LogWarning(
                "Ignoring direct cancellation of running deployment ticket {TicketId}; enqueue a control operation instead.",
                ticket.Id);
            return;
        }

        var shouldReleaseCapacity =
            ticket.Status == DeploymentQueueTicketStatus.Scheduled &&
            ticket.TargetNodeId is not null;
        var nodeId = ticket.TargetNodeId;
        var dockerSlots = ticket.DockerSlots;
        var vmSlots = ticket.VmSlots;

        ticket.Status = DeploymentQueueTicketStatus.Cancelled;
        ticket.Stage = DeploymentStage.Cancelled;
        ticket.StageMessage = TrimError(reason);
        ticket.ErrorMessage = TrimError(reason);
        ticket.CompletedAt = DateTimeOffset.UtcNow;
        ticket.ClaimOwner = null;
        ticket.ClaimExpiresAt = null;
        ticket.ProtectedPayload = null;

        using var correlationScope = _correlation.Begin(ticket.Id);
        _events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            OperationalEventCodes.Runtime.TicketCancelled,
            OperationalEventOutcome.Cancelled,
            "Deployment ticket was cancelled.",
            OperationalEventSeverity.Information,
            detail: new Dictionary<string, object?>
            {
                ["workload"] = ticket.Kind.ToString(),
                ["operation"] = ticket.Operation.ToString(),
                ["stage"] = ticket.Stage.ToString(),
                ["reasonCode"] = "ticket_cancelled"
            }));
        if (shouldReleaseCapacity)
            await ReleaseTicketCapacityAsync(ticket, nodeId, dockerSlots, vmSlots, token);
        await _context.SaveChangesAsync(token);
        PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "cancelled");

        _logger.SystemLog(
            $"Deployment queue ticket cancelled: ticket={ticket.Id}, kind={ticket.Kind}, node={nodeId}, reason={ticket.ErrorMessage}.",
            TaskStatus.Exit, LogLevel.Information);
    }

    public async Task CancelTeamLabRuntimeAsync(int runtimeId, string reason, CancellationToken token)
    {
        var tickets = await _context.DeploymentQueueTickets
            .Where(t => t.Kind == DeploymentQueueKind.TeamLabRuntime &&
                        t.TeamLabRuntimeId == runtimeId &&
                        ActiveStatuses.Contains(t.Status))
            .Select(t => t.Id)
            .ToListAsync(token);

        foreach (var ticketId in tickets)
            await CancelAsync(ticketId, reason, token);
    }

    async Task ReleaseTicketCapacityAsync(
        DeploymentQueueTicket ticket,
        Guid? nodeId,
        int dockerSlots,
        int vmSlots,
        CancellationToken token)
    {
        if (_capacity is null) return;
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            var shardSlots = await TeamLabCapacityFacts.LoadAsync(_context, runtimeId, token);
            if (shardSlots.Length > 0)
            {
                foreach (var slot in shardSlots)
                    await _capacity.ReleaseAsync(ticket.Id, slot.WorkerNodeId, token);
                return;
            }
        }

        if (nodeId is { } reservedNodeId)
            await _capacity.ReleaseAsync(ticket.Id, reservedNodeId, token);
    }

    async Task<int> GetQueuePositionAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Status != DeploymentQueueTicketStatus.Pending)
            return 0;

        var earlierCount = await _context.DeploymentQueueTickets
            .AsNoTracking()
            .Where(t => t.Kind == ticket.Kind && t.Status == DeploymentQueueTicketStatus.Pending)
            .Where(t => t.CreatedAt < ticket.CreatedAt)
            .CountAsync(token);

        var sameCreatedAtIds = await _context.DeploymentQueueTickets
            .AsNoTracking()
            .Where(t => t.Kind == ticket.Kind &&
                        t.Status == DeploymentQueueTicketStatus.Pending &&
                        t.CreatedAt == ticket.CreatedAt)
            .Select(t => t.Id)
            .ToListAsync(token);

        var sameCreatedAtPosition = sameCreatedAtIds
            .OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .TakeWhile(id => id != ticket.Id)
            .Count() + 1;

        return earlierCount + sameCreatedAtPosition;
    }

    static string TrimError(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "Deployment queue ticket was cancelled."
            : reason.Length <= 1024 ? reason : reason[..1024];

    static IOperationalEventWriter DefaultEvents(AppDbContext context) =>
        new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
}
