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

public class FleetCapacityReservationService
{
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

        var nodes = await BuildCandidateQuery(request)
            .ToListAsync(token);
        var node = SelectNode(nodes, request, dockerSlots, vmSlots);

        if (node is null)
            return FleetCapacityReservationResult.Failed(
                $"No schedulable node has enough capacity for Docker={dockerSlots}, VM={vmSlots}.");

        node.ReservedContainers += dockerSlots;
        node.ReservedVms += vmSlots;
        await _context.SaveChangesAsync(token);

        _logger.LogInformation(
            "Reserved fleet capacity on node {NodeId}: Docker={DockerSlots}, VM={VmSlots}",
            node.Id, dockerSlots, vmSlots);

        return FleetCapacityReservationResult.Reserved(node, dockerSlots, vmSlots);
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
                _context.ChangeTracker.Clear();
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
                _context.ChangeTracker.Clear();
            }
        }
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
            ? WeightedScheduler.GetTeamLabUnschedulableReason(node)
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
