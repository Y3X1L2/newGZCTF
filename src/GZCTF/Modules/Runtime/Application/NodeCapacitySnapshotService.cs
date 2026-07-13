using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Application;

public sealed record NodeCapacitySnapshot(
    WorkerNode Node,
    int LiveDocker,
    int LiveVm,
    int FactDocker,
    int FactVm,
    int ReservedDocker,
    int ReservedVm)
{
    public int CurrentDocker => Math.Max(LiveDocker, FactDocker);
    public int CurrentVm => Math.Max(LiveVm, FactVm);
    public int AllocatedDocker => CurrentDocker + ReservedDocker;
    public int AllocatedVm => CurrentVm + ReservedVm;
    public int AvailableDocker => Math.Max(0, Node.MaxContainers - AllocatedDocker);
    public int AvailableVm => Math.Max(0, Node.MaxVms - AllocatedVm);
}

public sealed class NodeCapacitySnapshotService
{
    readonly AppDbContext _context;
    readonly INodeLiveStateStore? _liveStateStore;

    public NodeCapacitySnapshotService(AppDbContext context) => _context = context;

    public NodeCapacitySnapshotService(AppDbContext context, INodeLiveStateStore liveStateStore)
    {
        _context = context;
        _liveStateStore = liveStateStore;
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
            .GroupBy(asset => new { NodeId = asset.WorkerNodeId!.Value, asset.Kind })
            .Select(group => new { group.Key.NodeId, group.Key.Kind, Count = group.Count() })
            .ToArrayAsync(token);
        var reservations = await _context.FleetCapacityReservations.AsNoTracking()
            .Where(item => nodeIds.Contains(item.WorkerNodeId) &&
                           item.Status == CapacityReservationStatus.Active && item.ExpiresAt > now)
            .GroupBy(item => item.WorkerNodeId)
            .Select(group => new
            {
                NodeId = group.Key,
                Docker = group.Sum(item => item.DockerSlots),
                Vm = group.Sum(item => item.VmSlots)
            })
            .ToDictionaryAsync(item => item.NodeId, token);

        var teamLabDocker = teamLabFacts.Where(item => item.Kind == TeamLabResourceKind.Docker)
            .ToDictionary(item => item.NodeId, item => item.Count);
        var teamLabVm = teamLabFacts.Where(item => item.Kind == TeamLabResourceKind.Vm)
            .ToDictionary(item => item.NodeId, item => item.Count);

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
            return new NodeCapacitySnapshot(
                node,
                liveDocker,
                liveVm,
                containerFacts.GetValueOrDefault(node.Id) + teamLabDocker.GetValueOrDefault(node.Id),
                vmFacts.GetValueOrDefault(node.Id) + teamLabVm.GetValueOrDefault(node.Id),
                reservation?.Docker ?? 0,
                reservation?.Vm ?? 0);
        }).ToArray();
    }
}
