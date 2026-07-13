using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public sealed record FleetCapacityRequest(
    NodeCapability RequiredCapability,
    int DockerSlots,
    int VmSlots,
    Guid? PreferredNodeId = null,
    bool RequireTeamLab = false);

public sealed record FleetCapacityReservationResult(
    bool Success,
    Guid? NodeId,
    WorkerNode? Node,
    int DockerSlots,
    int VmSlots,
    string Message)
{
    public static FleetCapacityReservationResult Reserved(WorkerNode node, int dockerSlots, int vmSlots) =>
        new(true, node.Id, node, dockerSlots, vmSlots, "Capacity reserved.");
    public static FleetCapacityReservationResult Failed(string message) =>
        new(false, null, null, 0, 0, message);
}

public sealed record FleetCapacityBatchItem(Guid NodeId, int DockerSlots, int VmSlots);

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

    public FleetCapacityReservationService(AppDbContext context, IDistributedLeaseProvider leaseProvider,
        ILogger<FleetCapacityReservationService> logger)
        : this(context, leaseProvider, new NodeCapacitySnapshotService(context),
            new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions())), logger)
    {
    }

    public FleetCapacityReservationService(AppDbContext context, IDistributedLeaseProvider leaseProvider,
        NodeCapacitySnapshotService snapshots, NodeEligibilityEvaluator eligibility,
        ILogger<FleetCapacityReservationService> logger)
    {
        this.context = context;
        this.leaseProvider = leaseProvider;
        this.snapshots = snapshots;
        this.eligibility = eligibility;
        this.logger = logger;
    }

    public async Task<FleetCapacityReservationResult> TryReserveAsync(Guid ticketId, FleetCapacityRequest request,
        CancellationToken token)
    {
        var dockerSlots = Math.Max(0, request.DockerSlots);
        var vmSlots = Math.Max(0, request.VmSlots);
        if (dockerSlots == 0 && vmSlots == 0)
            return FleetCapacityReservationResult.Failed("No capacity slots were requested.");

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
            return existingNode is null
                ? FleetCapacityReservationResult.Failed("Existing reservation references a missing node.")
                : FleetCapacityReservationResult.Reserved(existingNode, existing.DockerSlots, existing.VmSlots);
        }

        var candidates = (await snapshots.LoadAsync(token))
            .Where(item => request.PreferredNodeId is null || item.Node.Id == request.PreferredNodeId)
            .Where(item => eligibility.GetReason(item, request.RequiredCapability, dockerSlots, vmSlots,
                request.RequireTeamLab) is null)
            .OrderByDescending(item => eligibility.Score(item, dockerSlots, vmSlots))
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Node.Id)
            .ToArray();
        var selected = candidates.FirstOrDefault();
        if (selected is null)
            return FleetCapacityReservationResult.Failed(
                $"No schedulable node has enough capacity for Docker={dockerSlots}, VM={vmSlots}.");

        context.FleetCapacityReservations.Add(NewReservation(ticketId, selected.Node.Id, dockerSlots, vmSlots));
        await context.SaveChangesAsync(token);
        logger.LogInformation("Reserved capacity for ticket {TicketId} on node {NodeId}: Docker={Docker}, VM={Vm}",
            ticketId, selected.Node.Id, dockerSlots, vmSlots);
        return FleetCapacityReservationResult.Reserved(selected.Node, dockerSlots, vmSlots);
    }

    public async Task<FleetCapacityBatchReservationResult> TryReserveBatchAsync(Guid ticketId,
        IReadOnlyList<FleetCapacityBatchItem> items, bool requireTeamLab, CancellationToken token)
    {
        var normalized = items.GroupBy(item => item.NodeId)
            .Select(group => new FleetCapacityBatchItem(group.Key,
                group.Sum(item => Math.Max(0, item.DockerSlots)),
                group.Sum(item => Math.Max(0, item.VmSlots))))
            .Where(item => item.DockerSlots > 0 || item.VmSlots > 0)
            .OrderBy(item => item.NodeId).ToArray();
        if (normalized.Length == 0)
            return FleetCapacityBatchReservationResult.Failed("No capacity slots were requested.");

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
                .Select(item => FleetCapacityReservationResult.Reserved(existingNodes[item.WorkerNodeId],
                    item.DockerSlots, item.VmSlots)).ToArray());
        }

        var nodeIds = normalized.Select(item => item.NodeId).ToArray();
        var nodeSnapshots = (await snapshots.LoadAsync(token)).Where(item => nodeIds.Contains(item.Node.Id))
            .ToDictionary(item => item.Node.Id);
        foreach (var item in normalized)
        {
            if (!nodeSnapshots.TryGetValue(item.NodeId, out var snapshot))
                return FleetCapacityBatchReservationResult.Failed($"Node {item.NodeId} was not found.");
            var capability = (item.DockerSlots > 0 ? NodeCapability.Docker : NodeCapability.None) |
                             (item.VmSlots > 0 ? NodeCapability.Kvm : NodeCapability.None);
            if (eligibility.GetReason(snapshot, capability, item.DockerSlots, item.VmSlots, requireTeamLab) is not null)
                return FleetCapacityBatchReservationResult.Failed(
                    $"Node {snapshot.Node.Name} has insufficient capacity for Docker={item.DockerSlots}, VM={item.VmSlots}.");
        }

        foreach (var item in normalized)
            context.FleetCapacityReservations.Add(NewReservation(ticketId, item.NodeId, item.DockerSlots,
                item.VmSlots));
        await context.SaveChangesAsync(token);
        return FleetCapacityBatchReservationResult.Reserved(normalized.Select(item =>
            FleetCapacityReservationResult.Reserved(nodeSnapshots[item.NodeId].Node, item.DockerSlots, item.VmSlots)).ToArray());
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
        return await context.FleetCapacityReservations
            .Where(item => ticketIds.Contains(item.DeploymentQueueTicketId) &&
                           item.Status == CapacityReservationStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ExpiresAt, DateTimeOffset.UtcNow.Add(ReservationLifetime)), token);
    }

    public async Task ReconcileReservedAsync(Guid nodeId, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        await context.FleetCapacityReservations
            .Where(item => item.WorkerNodeId == nodeId && item.Status == CapacityReservationStatus.Active &&
                           item.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, CapacityReservationStatus.Expired)
                .SetProperty(item => item.ReleasedAt, now), token);
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
        await context.SaveChangesAsync(token);
    }

    static FleetCapacityReservation NewReservation(Guid ticketId, Guid nodeId, int dockerSlots, int vmSlots) =>
        new()
        {
            DeploymentQueueTicketId = ticketId,
            WorkerNodeId = nodeId,
            DockerSlots = dockerSlots,
            VmSlots = vmSlots,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ReservationLifetime)
        };

    async ValueTask<IDistributedLease> AcquireSchedulerLeaseAsync(CancellationToken token) =>
        await leaseProvider.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10), cancellationToken: token);
}
