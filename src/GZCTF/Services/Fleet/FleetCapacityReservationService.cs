using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services.Concurrency;
using Microsoft.EntityFrameworkCore;

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

public class FleetCapacityReservationService
{
    static readonly DeploymentQueueTicketStatus[] ActiveQueueStatuses =
    [
        DeploymentQueueTicketStatus.Pending,
        DeploymentQueueTicketStatus.Assigned,
        DeploymentQueueTicketStatus.Creating
    ];

    static readonly TargetStatus[] ReservedTargetStatuses =
    [
        TargetStatus.Assigned,
        TargetStatus.Creating
    ];

    static readonly TimeSpan DeploymentTargetReservationTimeout = TimeSpan.FromMinutes(30);

    readonly AppDbContext _context;
    readonly IDistributedLockService _lockService;
    readonly ILogger<FleetCapacityReservationService> _logger;

    public FleetCapacityReservationService(AppDbContext context, IDistributedLockService lockService,
        ILogger<FleetCapacityReservationService> logger)
    {
        _context = context;
        _lockService = lockService;
        _logger = logger;
    }

    public async Task<FleetCapacityReservationResult> TryReserveAsync(FleetCapacityRequest request,
        CancellationToken token)
    {
        var dockerSlots = Math.Max(0, request.DockerSlots);
        var vmSlots = Math.Max(0, request.VmSlots);

        if (dockerSlots == 0 && vmSlots == 0)
            return FleetCapacityReservationResult.Failed("No capacity slots were requested.");

        await using var _ = await AcquireSchedulerLockAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var nodes = await BuildCandidateQuery(request)
                .ToListAsync(token);
            var node = SelectNode(nodes, request, dockerSlots, vmSlots);

            if (node is null)
                return FleetCapacityReservationResult.Failed(
                    $"No schedulable node has enough capacity for Docker={dockerSlots}, VM={vmSlots}.");

            node.ReservedContainers += dockerSlots;
            node.ReservedVms += vmSlots;

            try
            {
                await _context.SaveChangesAsync(token);

                _logger.LogInformation(
                    "Reserved fleet capacity on node {NodeId}: Docker={DockerSlots}, VM={VmSlots}",
                    node.Id, dockerSlots, vmSlots);

                return FleetCapacityReservationResult.Reserved(node, dockerSlots, vmSlots);
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                _logger.LogWarning(ex,
                    "Capacity reservation conflicted; retrying reservation attempt {Attempt}.",
                    attempt + 1);
                DetachConflictedWorkerNodeEntries(ex);
            }
        }

        return FleetCapacityReservationResult.Failed("Capacity reservation conflicted repeatedly.");
    }

    public async Task<FleetCapacityBatchReservationResult> TryReserveBatchAsync(
        IReadOnlyList<FleetCapacityBatchItem> items,
        bool requireTeamLab,
        CancellationToken token)
    {
        var normalizedItems = items
            .GroupBy(item => item.NodeId)
            .Select(group => new FleetCapacityBatchItem(
                group.Key,
                group.Sum(item => Math.Max(0, item.DockerSlots)),
                group.Sum(item => Math.Max(0, item.VmSlots))))
            .Where(item => item.DockerSlots > 0 || item.VmSlots > 0)
            .OrderBy(item => item.NodeId)
            .ToArray();

        if (normalizedItems.Length == 0)
            return FleetCapacityBatchReservationResult.Failed("No capacity slots were requested.");

        await using var _ = await AcquireSchedulerLockAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var nodeIds = normalizedItems.Select(item => item.NodeId).ToArray();
            var nodes = await _context.WorkerNodes
                .Where(node => nodeIds.Contains(node.Id))
                .ToListAsync(token);
            var nodesById = nodes.ToDictionary(node => node.Id);

            foreach (var item in normalizedItems)
            {
                if (!nodesById.TryGetValue(item.NodeId, out var node))
                    return FleetCapacityBatchReservationResult.Failed($"Node {item.NodeId} was not found.");

                var requiredCapability = NodeCapability.None;
                if (item.DockerSlots > 0)
                    requiredCapability |= NodeCapability.Docker;
                if (item.VmSlots > 0)
                    requiredCapability |= NodeCapability.Kvm;

                var request = new FleetCapacityRequest(
                    requiredCapability,
                    item.DockerSlots,
                    item.VmSlots,
                    PreferredNodeId: item.NodeId,
                    RequireTeamLab: requireTeamLab);
                if (!CanReserve(node, request, item.DockerSlots, item.VmSlots))
                    return FleetCapacityBatchReservationResult.Failed(
                        $"Node {node.Name} has insufficient capacity for Docker={item.DockerSlots}, VM={item.VmSlots}.");
            }

            foreach (var item in normalizedItems)
            {
                var node = nodesById[item.NodeId];
                node.ReservedContainers += item.DockerSlots;
                node.ReservedVms += item.VmSlots;
            }

            try
            {
                await _context.SaveChangesAsync(token);

                var reservations = normalizedItems
                    .Select(item =>
                    {
                        var node = nodesById[item.NodeId];
                        return FleetCapacityReservationResult.Reserved(node, item.DockerSlots, item.VmSlots);
                    })
                    .ToArray();
                return FleetCapacityBatchReservationResult.Reserved(reservations);
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                _logger.LogWarning(ex,
                    "Batch capacity reservation conflicted; retrying reservation attempt {Attempt}.",
                    attempt + 1);
                DetachConflictedWorkerNodeEntries(ex);
            }
        }

        return FleetCapacityBatchReservationResult.Failed("Capacity reservation conflicted repeatedly.");
    }

    public async Task ReleaseAsync(Guid nodeId, int dockerSlots, int vmSlots, CancellationToken token)
    {
        var normalizedDockerSlots = Math.Max(0, dockerSlots);
        var normalizedVmSlots = Math.Max(0, vmSlots);
        if (normalizedDockerSlots == 0 && normalizedVmSlots == 0)
            return;

        await using var _ = await AcquireSchedulerLockAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == nodeId, token);
            if (node is null)
                return;

            node.ReservedContainers = Math.Max(0, node.ReservedContainers - normalizedDockerSlots);
            node.ReservedVms = Math.Max(0, node.ReservedVms - normalizedVmSlots);

            try
            {
                await _context.SaveChangesAsync(token);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                _logger.LogWarning(ex,
                    "Capacity release conflicted on node {NodeId}; retrying release attempt {Attempt}.",
                    nodeId, attempt + 1);
                DetachConflictedWorkerNodeEntries(ex);
            }
        }
    }

    public async Task ReleaseActiveAsync(Guid nodeId, int dockerSlots, int vmSlots, CancellationToken token)
    {
        var normalizedDockerSlots = Math.Max(0, dockerSlots);
        var normalizedVmSlots = Math.Max(0, vmSlots);
        if (normalizedDockerSlots == 0 && normalizedVmSlots == 0)
            return;

        await using var _ = await AcquireSchedulerLockAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == nodeId, token);
            if (node is null)
                return;

            node.CurrentContainers = Math.Max(0, node.CurrentContainers - normalizedDockerSlots);
            node.CurrentVms = Math.Max(0, node.CurrentVms - normalizedVmSlots);

            try
            {
                await _context.SaveChangesAsync(token);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                _logger.LogWarning(ex,
                    "Active capacity release conflicted on node {NodeId}; retrying release attempt {Attempt}.",
                    nodeId, attempt + 1);
                DetachConflictedWorkerNodeEntries(ex);
            }
        }
    }

    public async Task ConfirmAsync(Guid nodeId, int dockerSlots, int vmSlots, CancellationToken token)
    {
        var normalizedDockerSlots = Math.Max(0, dockerSlots);
        var normalizedVmSlots = Math.Max(0, vmSlots);
        if (normalizedDockerSlots == 0 && normalizedVmSlots == 0)
            return;

        await using var _ = await AcquireSchedulerLockAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == nodeId, token);
            if (node is null)
                return;

            var dockerToConfirm = Math.Min(node.ReservedContainers, normalizedDockerSlots);
            var vmToConfirm = Math.Min(node.ReservedVms, normalizedVmSlots);

            node.ReservedContainers = Math.Max(0, node.ReservedContainers - dockerToConfirm);
            node.ReservedVms = Math.Max(0, node.ReservedVms - vmToConfirm);
            node.CurrentContainers += dockerToConfirm;
            node.CurrentVms += vmToConfirm;

            try
            {
                await _context.SaveChangesAsync(token);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                _logger.LogWarning(ex,
                    "Capacity confirmation conflicted on node {NodeId}; retrying confirmation attempt {Attempt}.",
                    nodeId, attempt + 1);
                DetachConflictedWorkerNodeEntries(ex);
            }
        }
    }

    public async Task ReconcileReservedAsync(Guid nodeId, CancellationToken token)
    {
        await using var _ = await AcquireSchedulerLockAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == nodeId, token);
            if (node is null)
                return;

            var staleTargetCutoff = DateTimeOffset.UtcNow - DeploymentTargetReservationTimeout;
            var staleTargets = await _context.DeploymentTargets
                .Where(t => t.TargetNodeId == nodeId &&
                            t.Action == TargetAction.Create &&
                            ReservedTargetStatuses.Contains(t.Status) &&
                            t.CreatedAt < staleTargetCutoff)
                .ToListAsync(token);

            foreach (var target in staleTargets)
            {
                target.Status = TargetStatus.Failed;
                target.CompletedAt = DateTimeOffset.UtcNow;
                target.ErrorMessage ??= "Deployment target timed out before completion.";
            }

            var activeQueueSlots = await _context.DeploymentQueueTickets
                .Where(t => t.Kind != DeploymentQueueKind.TeamLabRuntime &&
                            t.TargetNodeId == nodeId &&
                            ActiveQueueStatuses.Contains(t.Status))
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Docker = g.Sum(t => t.DockerSlots),
                    Vm = g.Sum(t => t.VmSlots)
                })
                .FirstOrDefaultAsync(token);

            var activeTeamLabTickets = await _context.DeploymentQueueTickets.AsNoTracking()
                .Where(ticket => ticket.Kind == DeploymentQueueKind.TeamLabRuntime &&
                                 ActiveQueueStatuses.Contains(ticket.Status))
                .Select(ticket => new
                {
                    ticket.TeamLabRuntimeId,
                    ticket.TargetNodeId,
                    ticket.DockerSlots,
                    ticket.VmSlots
                })
                .ToArrayAsync(token);
            var teamLabDocker = 0;
            var teamLabVm = 0;
            var teamLabFacts = await TeamLabCapacityFacts.LoadManyAsync(
                _context,
                activeTeamLabTickets
                    .Where(ticket => ticket.TeamLabRuntimeId != null)
                    .Select(ticket => ticket.TeamLabRuntimeId!.Value)
                    .ToArray(),
                token);
            foreach (var ticket in activeTeamLabTickets)
            {
                var shardSlots = ticket.TeamLabRuntimeId is { } runtimeId
                    ? teamLabFacts.GetValueOrDefault(runtimeId) ?? []
                    : [];
                if (shardSlots.Length == 0)
                {
                    if (ticket.TargetNodeId == nodeId)
                    {
                        teamLabDocker += ticket.DockerSlots;
                        teamLabVm += ticket.VmSlots;
                    }
                    continue;
                }

                var local = shardSlots.FirstOrDefault(slot => slot.WorkerNodeId == nodeId);
                if (local is not null)
                {
                    teamLabDocker += local.DockerSlots;
                    teamLabVm += local.VmSlots;
                }
            }

            var activeTargetSlots = await _context.DeploymentTargets
                .Where(t => t.TargetNodeId == nodeId &&
                            t.Action == TargetAction.Create &&
                            ReservedTargetStatuses.Contains(t.Status) &&
                            t.CreatedAt >= staleTargetCutoff)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Docker = g.Count(t => t.Type == TargetType.Docker),
                    Vm = g.Count(t => t.Type == TargetType.Vm)
                })
                .FirstOrDefaultAsync(token);

            node.ReservedContainers = Math.Max(0,
                (activeQueueSlots?.Docker ?? 0) +
                teamLabDocker +
                (activeTargetSlots?.Docker ?? 0));
            node.ReservedVms = Math.Max(0,
                (activeQueueSlots?.Vm ?? 0) +
                teamLabVm +
                (activeTargetSlots?.Vm ?? 0));

            try
            {
                await _context.SaveChangesAsync(token);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                _logger.LogWarning(ex,
                    "Capacity reserved-slot reconciliation conflicted on node {NodeId}; retrying attempt {Attempt}.",
                    nodeId, attempt + 1);
                DetachConflictedWorkerNodeEntries(ex);
            }
        }
    }

    void DetachConflictedWorkerNodeEntries(DbUpdateConcurrencyException ex)
    {
        if (ex.Entries.Count > 0)
        {
            foreach (var entry in ex.Entries)
            {
                if (entry.Entity is not WorkerNode)
                    throw new InvalidOperationException(
                        "Unexpected non-worker concurrency conflict while updating fleet capacity.");

                entry.State = EntityState.Detached;
            }

            return;
        }

        foreach (var entry in _context.ChangeTracker.Entries<WorkerNode>())
            entry.State = EntityState.Detached;
    }

    IQueryable<WorkerNode> BuildCandidateQuery(FleetCapacityRequest request)
    {
        var cutoff = DateTimeOffset.UtcNow - WorkerNode.DefaultHeartbeatTimeout;
        var query = _context.WorkerNodes
            .Where(n => n.Status == NodeStatus.Online &&
                        (n.IsLocal || (n.LastHeartbeat.HasValue && n.LastHeartbeat >= cutoff)));

        if (request.PreferredNodeId is { } preferredNodeId)
            query = query.Where(n => n.Id == preferredNodeId);

        return query;
    }

    static WorkerNode? SelectNode(IEnumerable<WorkerNode> nodes, FleetCapacityRequest request,
        int dockerSlots, int vmSlots) =>
        nodes.Where(node => CanReserve(node, request, dockerSlots, vmSlots))
            .OrderByDescending(NodeScore)
            .FirstOrDefault();

    static bool CanReserve(WorkerNode node, FleetCapacityRequest request, int dockerSlots, int vmSlots)
    {
        var baseReason = request.RequireTeamLab
            ? WeightedScheduler.GetTeamLabFabricUnschedulableReason(node) ??
              WeightedScheduler.GetUnschedulableReason(node, request.RequiredCapability)
            : WeightedScheduler.GetUnschedulableReason(node, request.RequiredCapability);

        if (baseReason is not null)
            return false;

        return node.AllocatedContainers + dockerSlots <= node.MaxContainers &&
               node.AllocatedVms + vmSlots <= node.MaxVms;
    }

    static float NodeScore(WorkerNode node) =>
        1000f * (1 - Math.Clamp(node.CpuLoad, 0f, 1f)) +
        500f * (1 - Math.Clamp(node.MemoryLoad, 0f, 1f)) +
        200f * (1 - (float)node.AllocatedContainers / Math.Max(node.MaxContainers, 1)) +
        200f * (1 - (float)node.AllocatedVms / Math.Max(node.MaxVms, 1));

    async ValueTask<IAsyncDisposable> AcquireSchedulerLockAsync()
    {
        var releaser = await _lockService.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10));
        return new AsyncDisposableAdapter(releaser);
    }

    sealed class AsyncDisposableAdapter : IAsyncDisposable
    {
        readonly IDisposable _inner;

        public AsyncDisposableAdapter(IDisposable inner) => _inner = inner;

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
