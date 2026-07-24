using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Runtime.Application;

public sealed record NodeCapacitySnapshot(
    WorkerNode Node,
    int LiveDocker,
    int LiveVm,
    int FactDocker,
    int FactVm,
    int ReservedDocker,
    int ReservedVm,
    WorkloadResourceVector ResourceTotal = default,
    WorkloadResourceVector ResourceActual = default,
    WorkloadResourceVector ResourceReserved = default,
    WorkloadResourceVector ResourceSafetyMargin = default)
{
    public int CurrentDocker => Math.Max(LiveDocker, FactDocker);
    public int CurrentVm => Math.Max(LiveVm, FactVm);
    public int AllocatedDocker => CurrentDocker + ReservedDocker;
    public int AllocatedVm => CurrentVm + ReservedVm;
    public int AvailableDocker => Math.Max(0, Node.MaxContainers - AllocatedDocker);
    public int AvailableVm => Math.Max(0, Node.MaxVms - AllocatedVm);
    public WorkloadResourceVector Total => new(
        ResourceTotal.CpuUnits,
        ResourceTotal.MemoryMiB,
        ResourceTotal.StorageMiB,
        Math.Max(0, Node.MaxContainers),
        Math.Max(0, Node.MaxVms));
    public WorkloadResourceVector Actual => new(
        ResourceActual.CpuUnits,
        ResourceActual.MemoryMiB,
        ResourceActual.StorageMiB,
        CurrentDocker,
        CurrentVm);
    public WorkloadResourceVector Reserved => new(
        ResourceReserved.CpuUnits,
        ResourceReserved.MemoryMiB,
        ResourceReserved.StorageMiB,
        ReservedDocker,
        ReservedVm);
    public WorkloadResourceVector SafetyMargin => new(
        ResourceSafetyMargin.CpuUnits,
        ResourceSafetyMargin.MemoryMiB,
        ResourceSafetyMargin.StorageMiB,
        0,
        0);
    public WorkloadResourceVector Available => Total - Actual - Reserved - SafetyMargin;
    public WorkloadResourceVector AvailableIgnoringDynamicLoad => new(
        ResourceTotal.CpuUnits - ResourceReserved.CpuUnits,
        ResourceTotal.MemoryMiB - ResourceReserved.MemoryMiB,
        ResourceTotal.StorageMiB - ResourceReserved.StorageMiB,
        AvailableDocker,
        AvailableVm);
}

public sealed class NodeCapacitySnapshotService
{
    readonly AppDbContext _context;
    readonly INodeLiveStateStore? _liveStateStore;
    readonly RuntimeSchedulingOptions _options;

    public NodeCapacitySnapshotService(AppDbContext context)
    {
        _context = context;
        _options = new RuntimeSchedulingOptions();
    }

    public NodeCapacitySnapshotService(AppDbContext context, INodeLiveStateStore liveStateStore)
    {
        _context = context;
        _liveStateStore = liveStateStore;
        _options = new RuntimeSchedulingOptions();
    }

    public NodeCapacitySnapshotService(
        AppDbContext context,
        INodeLiveStateStore liveStateStore,
        IOptions<RuntimeSchedulingOptions> options)
    {
        _context = context;
        _liveStateStore = liveStateStore;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<NodeCapacitySnapshot>> LoadAsync(CancellationToken token)
    {
        var nodes = await _context.WorkerNodes.AsNoTracking().ToArrayAsync(token);
        if (nodes.Length == 0)
            return [];

        var nodeIds = nodes.Select(node => node.Id).ToArray();
        IReadOnlyDictionary<Guid, NodeLiveState> liveStates = _liveStateStore is null
            ? new Dictionary<Guid, NodeLiveState>()
            : await _liveStateStore.GetManyAsync(nodeIds, token);
        var now = DateTimeOffset.UtcNow;

        var containerFacts = await _context.Containers.AsNoTracking()
            .Where(container => container.NodeId != null && nodeIds.Contains(container.NodeId.Value) &&
                                container.Status != ContainerStatus.Destroyed)
            .GroupBy(container => container.NodeId!.Value)
            .Select(group => new { NodeId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.NodeId, item => item.Count, token);
        var vmFacts = await _context.VmInstances.AsNoTracking()
            .Where(vm => vm.NodeId != null && nodeIds.Contains(vm.NodeId.Value) &&
                         (vm.Status == VmInstanceStatus.Creating || vm.Status == VmInstanceStatus.Running))
            .GroupBy(vm => vm.NodeId!.Value)
            .Select(group => new { NodeId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.NodeId, item => item.Count, token);
        var teamLabFacts = await _context.TeamLabRuntimeAssets.AsNoTracking()
            .Where(asset => asset.WorkerNodeId != null && nodeIds.Contains(asset.WorkerNodeId.Value) &&
                            (asset.Status == TeamLabRuntimeStatus.Deploying ||
                             asset.Status == TeamLabRuntimeStatus.Probing ||
                             asset.Status == TeamLabRuntimeStatus.Running))
            .GroupBy(asset => new
            {
                asset.RuntimeId,
                asset.Generation,
                NodeId = asset.WorkerNodeId!.Value,
                asset.Kind
            })
            .Select(group => new
            {
                group.Key.RuntimeId,
                group.Key.Generation,
                group.Key.NodeId,
                group.Key.Kind,
                Count = group.Count()
            })
            .ToArrayAsync(token);
        var reservationRows = await _context.FleetCapacityReservations.AsNoTracking()
            .Where(item => nodeIds.Contains(item.WorkerNodeId) &&
                           item.Status == CapacityReservationStatus.Active && item.ExpiresAt > now)
            .Select(group => new
            {
                NodeId = group.WorkerNodeId,
                group.CpuUnits,
                group.MemoryMiB,
                group.StorageMiB,
                group.DockerSlots,
                group.VmSlots,
                RuntimeId = group.DeploymentQueueTicket.TeamLabRuntimeId,
                group.DeploymentQueueTicket.Generation
            })
            .ToArrayAsync(token);
        var teamLabFactCounts = teamLabFacts.ToDictionary(
            item => (item.RuntimeId, item.Generation, item.NodeId, item.Kind),
            item => item.Count);
        var reservations = reservationRows.GroupBy(item => item.NodeId).ToDictionary(
            group => group.Key,
            group => (
                CpuUnits: group.Sum(item => Math.Max(0, item.CpuUnits)),
                MemoryMiB: group.Sum(item => Math.Max(0, item.MemoryMiB)),
                StorageMiB: group.Sum(item => Math.Max(0, item.StorageMiB)),
                Docker: group.Sum(item => Math.Max(0, item.DockerSlots -
                    (item.RuntimeId is { } runtimeId
                        ? teamLabFactCounts.GetValueOrDefault(
                            (runtimeId, item.Generation, item.NodeId, TeamLabResourceKind.Docker))
                        : 0))),
                Vm: group.Sum(item => Math.Max(0, item.VmSlots -
                    (item.RuntimeId is { } runtimeId
                        ? teamLabFactCounts.GetValueOrDefault(
                            (runtimeId, item.Generation, item.NodeId, TeamLabResourceKind.Vm))
                        : 0)))));
        var teamLabDocker = teamLabFacts.Where(item => item.Kind == TeamLabResourceKind.Docker)
            .GroupBy(item => item.NodeId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count));
        var teamLabVm = teamLabFacts.Where(item => item.Kind == TeamLabResourceKind.Vm)
            .GroupBy(item => item.NodeId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count));

        return nodes.Select(node =>
        {
            var liveDocker = Math.Max(0, node.CurrentContainers);
            var liveVm = Math.Max(0, node.CurrentVms);
            if (liveStates.TryGetValue(node.Id, out var state) &&
                state.IsFresh(now, _liveStateStore!.FreshnessTtl))
            {
                liveDocker = Math.Max(0, state.CurrentContainers);
                liveVm = Math.Max(0, state.CurrentVms);
                node.CpuLoad = state.CpuLoad;
                node.MemoryLoad = state.MemoryLoad;
                node.LastHeartbeat = state.ReceivedAt;
                node.Status = NodeStatus.Online;
            }

            var reservation = reservations.GetValueOrDefault(node.Id);
            var manifest = AgentCapabilityEvaluator.Parse(node.CapabilityManifestJson);
            var totalCpuUnits = Math.Max(0L, (long)(manifest?.Host?.LogicalCpu ?? 0) * 10L);
            var totalMemoryMiB = Math.Max(0L, (manifest?.Host?.TotalMemoryBytes ?? 0) / (1024L * 1024L));
            var totalStorageMiB = Math.Max(0L,
                (manifest?.Host?.AvailableVmImageStorageBytes ?? 0) / (1024L * 1024L));
            var resourceTotal = new WorkloadResourceVector(
                totalCpuUnits, totalMemoryMiB, totalStorageMiB, 0, 0);
            var resourceActual = new WorkloadResourceVector(
                (long)Math.Ceiling(totalCpuUnits * Math.Clamp(node.CpuLoad, 0, 1)),
                (long)Math.Ceiling(totalMemoryMiB * Math.Clamp(node.MemoryLoad, 0, 1)),
                0, 0, 0);
            var resourceReserved = new WorkloadResourceVector(
                reservation.CpuUnits,
                reservation.MemoryMiB,
                reservation.StorageMiB,
                0,
                0);
            var resourceSafety = new WorkloadResourceVector(
                SafetyMargin(totalCpuUnits, _options.CpuRejectThreshold),
                SafetyMargin(totalMemoryMiB, _options.MemoryRejectThreshold),
                0, 0, 0);
            return new NodeCapacitySnapshot(
                node,
                liveDocker,
                liveVm,
                containerFacts.GetValueOrDefault(node.Id) + teamLabDocker.GetValueOrDefault(node.Id),
                vmFacts.GetValueOrDefault(node.Id) + teamLabVm.GetValueOrDefault(node.Id),
                reservation.Docker,
                reservation.Vm,
                resourceTotal,
                resourceActual,
                resourceReserved,
                resourceSafety);
        }).ToArray();
    }

    static long SafetyMargin(long total, float rejectThreshold) =>
        (long)Math.Ceiling(total * (1d - Math.Clamp(rejectThreshold, 0f, 1f)));
}
