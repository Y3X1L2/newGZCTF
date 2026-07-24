using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public sealed record FleetCapacityRequest(
    NodeCapability RequiredCapability,
    WorkloadResourceVector Resources,
    Guid? PreferredNodeId = null,
    bool RequireTeamLab = false);

public sealed record FleetCapacityReservationResult(
    bool Success,
    Guid? NodeId,
    WorkerNode? Node,
    int DockerSlots,
    int VmSlots,
    string Message,
    WorkloadResourceVector Resources = default)
{
    public static FleetCapacityReservationResult Reserved(WorkerNode node, WorkloadResourceVector resources) =>
        new(true, node.Id, node, resources.DockerSlots, resources.VmSlots, "Capacity reserved.", resources);
    public static FleetCapacityReservationResult Reserved(WorkerNode node, int dockerSlots, int vmSlots) =>
        Reserved(node, new WorkloadResourceVector(0, 0, 0, dockerSlots, vmSlots));
    public static FleetCapacityReservationResult Failed(string message) =>
        new(false, null, null, 0, 0, message);
}

public sealed record FleetCapacityBatchItem(Guid NodeId, WorkloadResourceVector Resources)
{
    public FleetCapacityBatchItem(Guid nodeId, int dockerSlots, int vmSlots)
        : this(nodeId, new WorkloadResourceVector(0, 0, 0, dockerSlots, vmSlots))
    {
    }

    public int DockerSlots => Resources.DockerSlots;
    public int VmSlots => Resources.VmSlots;
}

public sealed record FleetCapacityBatchReservationResult(
    bool Success,
    IReadOnlyList<FleetCapacityReservationResult> Reservations,
    string Message)
{
    public static FleetCapacityBatchReservationResult Reserved(
        IReadOnlyList<FleetCapacityReservationResult> reservations) =>
        new(true, reservations, "Capacity reserved.");
    public static FleetCapacityBatchReservationResult Failed(string message) => new(false, [], message);
}

public sealed class FleetCapacityReservationService
{
    static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(30);
    readonly AppDbContext context;
    readonly IDistributedLeaseProvider leaseProvider;
    readonly NodeCapacitySnapshotService snapshots;
    readonly NodeEligibilityEvaluator eligibility;
    readonly ILogger<FleetCapacityReservationService> logger;
    readonly IOperationalEventWriter events;

    public FleetCapacityReservationService(AppDbContext context, IDistributedLeaseProvider leaseProvider,
        ILogger<FleetCapacityReservationService> logger)
        : this(context, leaseProvider, new NodeCapacitySnapshotService(context),
            new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions())),
            new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance), logger)
    {
    }

    public FleetCapacityReservationService(AppDbContext context, IDistributedLeaseProvider leaseProvider,
        NodeCapacitySnapshotService snapshots, NodeEligibilityEvaluator eligibility,
        ILogger<FleetCapacityReservationService> logger)
        : this(context, leaseProvider, snapshots, eligibility,
            new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance), logger)
    {
    }

    public FleetCapacityReservationService(AppDbContext context, IDistributedLeaseProvider leaseProvider,
        NodeCapacitySnapshotService snapshots, NodeEligibilityEvaluator eligibility,
        IOperationalEventWriter events,
        ILogger<FleetCapacityReservationService> logger)
    {
        this.context = context;
        this.leaseProvider = leaseProvider;
        this.snapshots = snapshots;
        this.eligibility = eligibility;
        this.events = events;
        this.logger = logger;
    }

    public async Task<FleetCapacityReservationResult> TryReserveAsync(Guid ticketId, FleetCapacityRequest request,
        CancellationToken token)
    {
        var requested = request.Resources;
        if (!requested.IsNonNegative)
            return await CapacityBlockedAsync(ticketId, request.PreferredNodeId,
                "Capacity requests cannot contain negative resources.", token);
        if (requested == WorkloadResourceVector.Zero)
            return await CapacityBlockedAsync(ticketId, request.PreferredNodeId,
                "No capacity resources were requested.", token);

        await using var lease = await AcquireSchedulerLeaseAsync(token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = linked.Token;

        var existing = await context.FleetCapacityReservations.AsNoTracking()
            .FirstOrDefaultAsync(item => item.DeploymentQueueTicketId == ticketId &&
                                         item.Status == CapacityReservationStatus.Active, token);
        if (existing is not null)
        {
            var existingNode = await context.WorkerNodes.FirstOrDefaultAsync(item => item.Id == existing.WorkerNodeId,
                token);
            if (existingNode is null)
                return await CapacityBlockedAsync(ticketId, existing.WorkerNodeId,
                    "Existing reservation references a missing node.", token,
                    OperationalEventCodes.Capacity.Conflict, OperationalErrorCodes.RuntimeIdentityConflict,
                    OperationalErrorCategory.Conflict, false);
            return FleetCapacityReservationResult.Reserved(existingNode, ToVector(existing));
        }

        var candidates = (await snapshots.LoadAsync(token))
            .Where(item => request.PreferredNodeId is null || item.Node.Id == request.PreferredNodeId)
            .Where(item => eligibility.GetReason(item, request.RequiredCapability, requested,
                request.RequireTeamLab) is null)
            .OrderByDescending(item => eligibility.Score(item, requested))
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Node.Id)
            .ToArray();
        var selected = candidates.FirstOrDefault();
        if (selected is null)
            return await CapacityBlockedAsync(ticketId, request.PreferredNodeId,
                $"No schedulable node has enough capacity for {Format(requested)}.", token);

        context.FleetCapacityReservations.Add(NewReservation(ticketId, selected.Node.Id, requested));
        if (await LoadTicketAsync(ticketId, token) is { } ticket)
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Capacity.Reserved,
                OperationalEventOutcome.Succeeded,
                "Node capacity was reserved for the runtime ticket.",
                workerNodeId: selected.Node.Id,
                detail: CapacityDetail(ticket, requested)));
        await context.SaveChangesAsync(token);
        logger.LogInformation("Reserved capacity for ticket {TicketId} on node {NodeId}: Docker={Docker}, VM={Vm}",
            ticketId, selected.Node.Id, requested.DockerSlots, requested.VmSlots);
        return FleetCapacityReservationResult.Reserved(selected.Node, requested);
    }

    public async Task<FleetCapacityBatchReservationResult> TryReserveBatchAsync(Guid ticketId,
        IReadOnlyList<FleetCapacityBatchItem> items, bool requireTeamLab, CancellationToken token)
    {
        if (items.Any(item => !item.Resources.IsNonNegative))
        {
            await CapacityBlockedAsync(ticketId, null,
                "Capacity requests cannot contain negative resources.", token);
            return FleetCapacityBatchReservationResult.Failed(
                "Capacity requests cannot contain negative resources.");
        }
        var normalized = items.GroupBy(item => item.NodeId)
            .Select(group => new FleetCapacityBatchItem(group.Key,
                group.Aggregate(WorkloadResourceVector.Zero, (sum, item) => sum + item.Resources)))
            .Where(item => item.Resources != WorkloadResourceVector.Zero)
            .OrderBy(item => item.NodeId).ToArray();
        if (normalized.Length == 0)
        {
            await CapacityBlockedAsync(ticketId, null, "No capacity slots were requested.", token);
            return FleetCapacityBatchReservationResult.Failed("No capacity slots were requested.");
        }

        await using var lease = await AcquireSchedulerLeaseAsync(token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = linked.Token;

        var existing = await context.FleetCapacityReservations.AsNoTracking()
            .Where(item => item.DeploymentQueueTicketId == ticketId &&
                           item.Status == CapacityReservationStatus.Active)
            .ToArrayAsync(token);
        if (existing.Length > 0)
        {
            var existingNodeIds = existing.Select(item => item.WorkerNodeId).ToArray();
            var existingNodes = await context.WorkerNodes.Where(item => existingNodeIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, token);
            return FleetCapacityBatchReservationResult.Reserved(existing
                .Where(item => existingNodes.ContainsKey(item.WorkerNodeId))
                .Select(item => FleetCapacityReservationResult.Reserved(
                    existingNodes[item.WorkerNodeId], ToVector(item))).ToArray());
        }

        var nodeIds = normalized.Select(item => item.NodeId).ToArray();
        var nodeSnapshots = (await snapshots.LoadAsync(token)).Where(item => nodeIds.Contains(item.Node.Id))
            .ToDictionary(item => item.Node.Id);
        foreach (var item in normalized)
        {
            if (!nodeSnapshots.TryGetValue(item.NodeId, out var snapshot))
            {
                await CapacityBlockedAsync(ticketId, item.NodeId, $"Node {item.NodeId} was not found.", token,
                    OperationalEventCodes.Capacity.Conflict, OperationalErrorCodes.NodeNotFound,
                    OperationalErrorCategory.NodeUnavailable, false);
                return FleetCapacityBatchReservationResult.Failed($"Node {item.NodeId} was not found.");
            }
            var capability = (item.DockerSlots > 0 ? NodeCapability.Docker : NodeCapability.None) |
                             (item.VmSlots > 0 ? NodeCapability.Kvm : NodeCapability.None);
            if (eligibility.GetReason(snapshot, capability, item.Resources, requireTeamLab) is not null)
            {
                await CapacityBlockedAsync(ticketId, item.NodeId,
                    $"Node {snapshot.Node.Name} has insufficient capacity for {Format(item.Resources)}.",
                    token);
                return FleetCapacityBatchReservationResult.Failed(
                    $"Node {snapshot.Node.Name} has insufficient capacity for {Format(item.Resources)}.");
            }
        }

        foreach (var item in normalized)
            context.FleetCapacityReservations.Add(NewReservation(ticketId, item.NodeId, item.Resources));
        if (await LoadTicketAsync(ticketId, token) is { } batchTicket)
            foreach (var item in normalized)
                events.Append(RuntimeOperationalEvents.Ticket(
                    batchTicket,
                    OperationalEventCodes.Capacity.Reserved,
                    OperationalEventOutcome.Succeeded,
                    "Node capacity was reserved for a runtime shard.",
                    workerNodeId: item.NodeId,
                    detail: CapacityDetail(batchTicket, item.Resources)));
        await context.SaveChangesAsync(token);
        return FleetCapacityBatchReservationResult.Reserved(normalized.Select(item =>
            FleetCapacityReservationResult.Reserved(nodeSnapshots[item.NodeId].Node, item.Resources)).ToArray());
    }

    public Task ConfirmAsync(Guid ticketId, Guid nodeId, CancellationToken token) =>
        FinishReservationAsync(ticketId, nodeId, CapacityReservationStatus.Confirmed, token);

    public Task ReleaseAsync(Guid ticketId, Guid nodeId, CancellationToken token) =>
        FinishReservationAsync(ticketId, nodeId, CapacityReservationStatus.Released, token);

    public Task<int> RenewAsync(Guid ticketId, CancellationToken token) =>
        context.FleetCapacityReservations
            .Where(item => item.DeploymentQueueTicketId == ticketId &&
                           item.Status == CapacityReservationStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ExpiresAt, DateTimeOffset.UtcNow.Add(ReservationLifetime)), token);

    public async Task<int> RenewActiveTicketReservationsAsync(CancellationToken token)
    {
        var ticketIds = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Status == DeploymentQueueTicketStatus.Scheduled ||
                             ticket.Status == DeploymentQueueTicketStatus.Running)
            .Select(ticket => ticket.Id)
            .ToArrayAsync(token);
        if (ticketIds.Length == 0)
            return 0;
        if (!context.Database.IsRelational())
        {
            var reservations = await context.FleetCapacityReservations
                .Where(item => ticketIds.Contains(item.DeploymentQueueTicketId) &&
                               item.Status == CapacityReservationStatus.Active)
                .ToArrayAsync(token);
            var expiresAt = DateTimeOffset.UtcNow.Add(ReservationLifetime);
            foreach (var reservation in reservations)
                reservation.ExpiresAt = expiresAt;
            await context.SaveChangesAsync(token);
            return reservations.Length;
        }
        return await context.FleetCapacityReservations
            .Where(item => ticketIds.Contains(item.DeploymentQueueTicketId) &&
                           item.Status == CapacityReservationStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ExpiresAt, DateTimeOffset.UtcNow.Add(ReservationLifetime)), token);
    }

    public async Task ReconcileReservedAsync(Guid nodeId, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await context.FleetCapacityReservations
            .Where(item => item.WorkerNodeId == nodeId && item.Status == CapacityReservationStatus.Active &&
                           item.ExpiresAt <= now)
            .ToArrayAsync(token);
        if (expired.Length == 0)
            return;
        var ticketIds = expired.Select(item => item.DeploymentQueueTicketId).Distinct().ToArray();
        var tickets = await context.DeploymentQueueTickets
            .Where(ticket => ticketIds.Contains(ticket.Id))
            .ToDictionaryAsync(ticket => ticket.Id, token);
        foreach (var reservation in expired)
        {
            reservation.Status = CapacityReservationStatus.Expired;
            reservation.ReleasedAt = now;
            if (tickets.TryGetValue(reservation.DeploymentQueueTicketId, out var ticket))
                events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    OperationalEventCodes.Capacity.Expired,
                    OperationalEventOutcome.Recovered,
                    "A runtime capacity reservation expired.",
                    OperationalEventSeverity.Warning,
                    new OperationalError(
                        OperationalErrorCategory.Capacity,
                        OperationalErrorCodes.RuntimeCapacityExhausted,
                        "Capacity reservation expired.",
                        true,
                        WorkerNodeId: nodeId,
                        Operation: "runtime.capacity.expire"),
                    nodeId,
                    CapacityDetail(ticket, ToVector(reservation))));
        }
        await context.SaveChangesAsync(token);
    }

    async Task FinishReservationAsync(Guid ticketId, Guid nodeId, CapacityReservationStatus status,
        CancellationToken token)
    {
        await using var lease = await AcquireSchedulerLeaseAsync(token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = linked.Token;
        var reservation = await context.FleetCapacityReservations.FirstOrDefaultAsync(item =>
            item.DeploymentQueueTicketId == ticketId && item.WorkerNodeId == nodeId &&
            item.Status == CapacityReservationStatus.Active, token);
        if (reservation is null)
            return;
        reservation.Status = status;
        reservation.ReleasedAt = DateTimeOffset.UtcNow;
        if (await LoadTicketAsync(ticketId, token) is { } ticket)
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                status == CapacityReservationStatus.Confirmed
                    ? OperationalEventCodes.Capacity.Confirmed
                    : OperationalEventCodes.Capacity.Released,
                OperationalEventOutcome.Succeeded,
                status == CapacityReservationStatus.Confirmed
                    ? "Runtime capacity reservation was confirmed."
                    : "Runtime capacity reservation was released.",
                workerNodeId: nodeId,
                detail: CapacityDetail(ticket, ToVector(reservation))));
        await context.SaveChangesAsync(token);
    }

    static FleetCapacityReservation NewReservation(
        Guid ticketId,
        Guid nodeId,
        WorkloadResourceVector resources) =>
        new()
        {
            DeploymentQueueTicketId = ticketId,
            WorkerNodeId = nodeId,
            CpuUnits = resources.CpuUnits,
            MemoryMiB = resources.MemoryMiB,
            StorageMiB = resources.StorageMiB,
            DockerSlots = resources.DockerSlots,
            VmSlots = resources.VmSlots,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ReservationLifetime)
        };

    static WorkloadResourceVector ToVector(FleetCapacityReservation reservation) =>
        new(
            reservation.CpuUnits,
            reservation.MemoryMiB,
            reservation.StorageMiB,
            reservation.DockerSlots,
            reservation.VmSlots);

    async ValueTask<IDistributedLease> AcquireSchedulerLeaseAsync(CancellationToken token) =>
        await leaseProvider.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10), cancellationToken: token);

    async Task<DeploymentQueueTicket?> LoadTicketAsync(Guid ticketId, CancellationToken token) =>
        await context.DeploymentQueueTickets.SingleOrDefaultAsync(ticket => ticket.Id == ticketId, token);

    async Task<FleetCapacityReservationResult> CapacityBlockedAsync(
        Guid ticketId,
        Guid? nodeId,
        string message,
        CancellationToken token,
        string eventCode = OperationalEventCodes.Capacity.Blocked,
        string errorCode = OperationalErrorCodes.RuntimeCapacityExhausted,
        OperationalErrorCategory category = OperationalErrorCategory.Capacity,
        bool retryable = true)
    {
        if (await LoadTicketAsync(ticketId, token) is { } ticket)
        {
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                eventCode,
                eventCode == OperationalEventCodes.Capacity.Conflict
                    ? OperationalEventOutcome.Failed
                    : OperationalEventOutcome.Blocked,
                "Runtime capacity reservation was blocked.",
                OperationalEventSeverity.Warning,
                new OperationalError(category, errorCode, message, retryable,
                    WorkerNodeId: nodeId, Operation: "runtime.capacity.reserve"),
                nodeId,
                CapacityDetail(ticket, new WorkloadResourceVector(
                    0, 0, 0, ticket.DockerSlots, ticket.VmSlots))));
            await context.SaveChangesAsync(token);
        }
        return FleetCapacityReservationResult.Failed(message);
    }

    static IReadOnlyDictionary<string, object?> CapacityDetail(
        DeploymentQueueTicket ticket,
        WorkloadResourceVector resources) =>
        new Dictionary<string, object?>
        {
            ["workload"] = ticket.Kind.ToString(),
            ["operation"] = ticket.Operation.ToString(),
            ["stage"] = ticket.Stage.ToString(),
            ["cpuUnits"] = resources.CpuUnits,
            ["memoryMiB"] = resources.MemoryMiB,
            ["storageMiB"] = resources.StorageMiB,
            ["dockerSlots"] = resources.DockerSlots,
            ["vmSlots"] = resources.VmSlots,
            ["attempt"] = ticket.AttemptCount
        };

    static string Format(WorkloadResourceVector resources) =>
        $"CPU={resources.CpuUnits}, memory={resources.MemoryMiB} MiB, " +
        $"storage={resources.StorageMiB} MiB, Docker={resources.DockerSlots}, VM={resources.VmSlots}";
}
