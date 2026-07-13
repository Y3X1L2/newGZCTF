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

    public async Task<int> RecoverStaleCreatingTicketsAsync(TimeSpan staleAfter, CancellationToken token)
    {
        var cutoff = DateTimeOffset.UtcNow - staleAfter;
        var tickets = await _context.DeploymentQueueTickets
            .Where(t => t.Status == DeploymentQueueTicketStatus.Running)
            .Where(t => (t.StartedAt ?? t.AssignedAt ?? t.CreatedAt) < cutoff)
            .ToListAsync(token);
        List<Guid> replayIds = [];

        foreach (var ticket in tickets)
        {
            var recovery = await InspectRecoveryAsync(ticket, token);
            ticket.ClaimOwner = null;
            ticket.ClaimExpiresAt = null;
            if (recovery == RuntimeRecoveryDecision.Completed)
            {
                ticket.Status = DeploymentQueueTicketStatus.Succeeded;
                ticket.Stage = DeploymentStage.Ready;
                ticket.StageMessage = "Runtime fact confirmed after execution claim expired.";
                ticket.ErrorMessage = null;
                ticket.CompletedAt = DateTimeOffset.UtcNow;
                ticket.ProtectedPayload = null;
                _events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    OperationalEventCodes.Runtime.ExecutionClaimRecovered,
                    OperationalEventOutcome.Recovered,
                    "Expired execution claim was completed from runtime facts."));
                if (_capacity is not null && ticket.Operation == RuntimeOperationKind.Create)
                    await ConfirmTicketCapacityAsync(ticket, token);
                PlatformTelemetry.RecordRecoveryDecision("completed", ticket.Kind.ToString());
                _logger.SystemLog(
                    $"Stale deployment ticket confirmed from runtime facts: ticket={ticket.Id}, kind={ticket.Kind}, node={ticket.TargetNodeId}.",
                    TaskStatus.Success, LogLevel.Information);
                continue;
            }

            if (recovery == RuntimeRecoveryDecision.SafeReplay && ticket.TargetNodeId is not null)
            {
                ticket.Status = DeploymentQueueTicketStatus.Scheduled;
                ticket.Stage = DeploymentStage.NodeExecutionWaiting;
                ticket.StageMessage = "Execution claim expired; operation is safe to replay by stable identity.";
                ticket.ErrorMessage = null;
                ticket.StartedAt = null;
                ticket.CompletedAt = null;
                ticket.AttemptCount++;
                replayIds.Add(ticket.Id);
                _events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    OperationalEventCodes.Runtime.ExecutionReplayQueued,
                    OperationalEventOutcome.Recovered,
                    "Expired execution claim was queued for idempotent replay.",
                    OperationalEventSeverity.Warning));
                PlatformTelemetry.RecordRecoveryDecision("safe_replay", ticket.Kind.ToString());
                _logger.SystemLog(
                    $"Stale deployment ticket scheduled for idempotent replay: ticket={ticket.Id}, kind={ticket.Kind}, node={ticket.TargetNodeId}.",
                    TaskStatus.Pending, LogLevel.Warning);
                continue;
            }

            ticket.Status = DeploymentQueueTicketStatus.Failed;
            ticket.Stage = DeploymentStage.Failed;
            ticket.ErrorMessage = "Execution claim expired and runtime facts could not prove completion or safe replay.";
            ticket.StageMessage = ticket.ErrorMessage;
            ticket.CompletedAt = DateTimeOffset.UtcNow;
            ticket.ProtectedPayload = null;
            var error = RuntimeOperationalEvents.Failure(
                ticket,
                "runtime.recover",
                code: OperationalErrorCodes.RuntimeIdentityConflict,
                category: OperationalErrorCategory.Conflict);
            ticket.ErrorCategory = error.Category;
            ticket.ErrorCode = error.Code;
            ticket.Retryable = error.Retryable;
            _events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Runtime.ExecutionFailedClosed,
                OperationalEventOutcome.Failed,
                "Expired execution claim failed closed.",
                OperationalEventSeverity.Warning,
                error));
            await ReleaseTicketCapacityAsync(ticket, ticket.TargetNodeId, ticket.DockerSlots, ticket.VmSlots, token);
            PlatformTelemetry.RecordRecoveryDecision("failed_closed", ticket.Kind.ToString());
            _logger.SystemLog(
                $"Stale deployment ticket failed closed: ticket={ticket.Id}, kind={ticket.Kind}, node={ticket.TargetNodeId}.",
                TaskStatus.Failed, LogLevel.Warning);
        }

        await _context.SaveChangesAsync(token);
        foreach (var replayId in replayIds)
            await _wakeup.NotifyAsync(replayId, token);

        return tickets.Count;
    }

    async Task<RuntimeRecoveryDecision> InspectRecoveryAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.Operation != RuntimeOperationKind.Create)
            return ticket.Operation is RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy
                ? RuntimeRecoveryDecision.SafeReplay
                : RuntimeRecoveryDecision.FailClosed;

        return ticket.Kind switch
        {
            DeploymentQueueKind.GameContainer => await InspectGameContainerAsync(ticket, token),
            DeploymentQueueKind.ExerciseContainer or DeploymentQueueKind.TrainingContainer =>
                await InspectExerciseContainerAsync(ticket, token),
            DeploymentQueueKind.AwdpContainer => await InspectAwdpContainerAsync(ticket, token),
            DeploymentQueueKind.ChallengeTestContainer => await InspectChallengeTestContainerAsync(ticket, token),
            DeploymentQueueKind.VirtualMachine => await InspectVmAsync(ticket, token),
            DeploymentQueueKind.TeamLabRuntime => await InspectTeamLabAsync(ticket, token),
            _ => RuntimeRecoveryDecision.FailClosed
        };
    }

    async Task<RuntimeRecoveryDecision> InspectGameContainerAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.GameId is not { } gameId || ticket.OwnerTeamId is not { } teamId ||
            ticket.ChallengeId is not { } challengeId)
            return RuntimeRecoveryDecision.FailClosed;
        var status = await _context.GameInstances.AsNoTracking()
            .Where(instance => instance.ChallengeId == challengeId &&
                               instance.Participation.GameId == gameId &&
                               instance.Participation.TeamId == teamId)
            .Select(instance => instance.Container == null ? (ContainerStatus?)null : instance.Container.Status)
            .SingleOrDefaultAsync(token);
        return status == ContainerStatus.Running
            ? RuntimeRecoveryDecision.Completed
            : RuntimeRecoveryDecision.SafeReplay;
    }

    async Task<RuntimeRecoveryDecision> InspectExerciseContainerAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.OwnerUserId is not { } userId || ticket.ChallengeId is not { } challengeId)
            return RuntimeRecoveryDecision.FailClosed;
        var status = await _context.ExerciseInstances.AsNoTracking()
            .Where(instance => instance.UserId == userId && instance.ExerciseId == challengeId)
            .Select(instance => instance.Container == null ? (ContainerStatus?)null : instance.Container.Status)
            .SingleOrDefaultAsync(token);
        return status == ContainerStatus.Running
            ? RuntimeRecoveryDecision.Completed
            : RuntimeRecoveryDecision.SafeReplay;
    }

    async Task<RuntimeRecoveryDecision> InspectAwdpContainerAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.AwdpServiceInstanceId is not { } instanceId)
            return RuntimeRecoveryDecision.FailClosed;
        var status = await _context.AwdpServiceInstances.AsNoTracking()
            .Where(instance => instance.Id == instanceId)
            .Select(instance => instance.Container == null ? (ContainerStatus?)null : instance.Container.Status)
            .SingleOrDefaultAsync(token);
        return status == ContainerStatus.Running
            ? RuntimeRecoveryDecision.Completed
            : RuntimeRecoveryDecision.SafeReplay;
    }

    async Task<RuntimeRecoveryDecision> InspectChallengeTestContainerAsync(
        DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.SubjectType != "challenge-test-container" ||
            ticket.GameId is not { } gameId || ticket.ChallengeId is not { } challengeId)
            return RuntimeRecoveryDecision.FailClosed;
        var status = await _context.GameChallenges.AsNoTracking()
            .Where(challenge => challenge.GameId == gameId && challenge.Id == challengeId)
            .Select(challenge => challenge.TestContainer == null
                ? (ContainerStatus?)null
                : challenge.TestContainer.Status)
            .SingleOrDefaultAsync(token);
        return status == ContainerStatus.Running
            ? RuntimeRecoveryDecision.Completed
            : RuntimeRecoveryDecision.SafeReplay;
    }

    async Task<RuntimeRecoveryDecision> InspectVmAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.VmInstanceId is not { } vmId)
            return RuntimeRecoveryDecision.FailClosed;
        var status = await _context.VmInstances.AsNoTracking()
            .Where(instance => instance.Id == vmId)
            .Select(instance => (VmInstanceStatus?)instance.Status)
            .SingleOrDefaultAsync(token);
        return status == VmInstanceStatus.Running
            ? RuntimeRecoveryDecision.Completed
            : status is VmInstanceStatus.Creating or VmInstanceStatus.Error
                ? RuntimeRecoveryDecision.SafeReplay
                : RuntimeRecoveryDecision.FailClosed;
    }

    async Task<RuntimeRecoveryDecision> InspectTeamLabAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.TeamLabRuntimeId is not { } runtimeId)
            return RuntimeRecoveryDecision.FailClosed;
        var status = await _context.TeamLabRuntimes.AsNoTracking()
            .Where(runtime => runtime.Id == runtimeId)
            .Select(runtime => (TeamLabRuntimeStatus?)runtime.Status)
            .SingleOrDefaultAsync(token);
        return status == TeamLabRuntimeStatus.Running
            ? RuntimeRecoveryDecision.Completed
            : status is TeamLabRuntimeStatus.Scheduled or TeamLabRuntimeStatus.Deploying or TeamLabRuntimeStatus.Failed
                ? RuntimeRecoveryDecision.SafeReplay
                : RuntimeRecoveryDecision.FailClosed;
    }

    async Task ConfirmTicketCapacityAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (_capacity is null)
            return;
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            foreach (var slot in await TeamLabCapacityFacts.LoadAsync(_context, runtimeId, token))
                await _capacity.ConfirmAsync(ticket.Id, slot.WorkerNodeId, token);
            return;
        }
        if (ticket.TargetNodeId is { } nodeId)
            await _capacity.ConfirmAsync(ticket.Id, nodeId, token);
    }

    enum RuntimeRecoveryDecision : byte
    {
        Completed,
        SafeReplay,
        FailClosed
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
