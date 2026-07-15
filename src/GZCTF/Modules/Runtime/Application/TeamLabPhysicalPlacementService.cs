using System.Diagnostics;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.Modules.Runtime.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Runtime.Application;

public sealed class TeamLabPhysicalPlacementService(
    AppDbContext context,
    IDistributedLeaseProvider leaseProvider,
    NodeCapacitySnapshotService snapshots,
    NodeEligibilityEvaluator eligibility,
    IOperationalEventWriter events,
    TeamLabEventRecorder teamLabEvents,
    IOptions<TeamLabNetworkConfig> options)
{
    static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(30);
    readonly TeamLabNetworkConfig _network = options.Value;

    public async Task<FleetCapacityReservationResult> BindAndReserveAsync(Guid ticketId, int runtimeId,
        CancellationToken token)
    {
        using var activity = PlatformTelemetry.TeamLabActivitySource.StartActivity(
            "teamlab.placement", ActivityKind.Internal);
        activity?.SetTag("gzctf.deployment_ticket_id", ticketId.ToString());
        activity?.SetTag("gzctf.teamlab_runtime_id", runtimeId);
        await using var lease = await leaseProvider.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10),
            cancellationToken: token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = linked.Token;

        var existingReservations = await context.FleetCapacityReservations.AsNoTracking()
            .Where(item => item.DeploymentQueueTicketId == ticketId &&
                           item.Status == CapacityReservationStatus.Active)
            .ToArrayAsync(token);
        if (existingReservations.Length > 0)
        {
            var runtimeEntryNode = await context.TeamLabRuntimes.AsNoTracking()
                .Where(item => item.Id == runtimeId)
                .Select(item => item.Shards.Where(shard => shard.Id == item.EntryShardId)
                    .Select(shard => (Guid?)shard.WorkerNodeId).FirstOrDefault())
                .SingleOrDefaultAsync(token);
            var selected = existingReservations.FirstOrDefault(item => item.WorkerNodeId == runtimeEntryNode) ??
                           existingReservations[0];
            var node = await context.WorkerNodes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == selected.WorkerNodeId, token);
            return node is null
                ? FleetCapacityReservationResult.Failed("Existing TeamLab reservation references a missing node.")
                : FleetCapacityReservationResult.Reserved(node, selected.DockerSlots, selected.VmSlots);
        }

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(token)
            : null;
        var runtime = await context.TeamLabRuntimes
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .Include(item => item.PublicUdpMapping)
            .Include(item => item.Events)
            .SingleOrDefaultAsync(item => item.Id == runtimeId, token);
        if (runtime is null)
            return FleetCapacityReservationResult.Failed("TeamLab runtime was not found.");

        var generationNetworks = runtime.Networks.Where(item => item.Generation == runtime.Generation).ToArray();
        var generationAssets = runtime.Assets.Where(item => item.Generation == runtime.Generation).ToArray();
        if (generationNetworks.Length == 0)
            return FleetCapacityReservationResult.Failed("TeamLab runtime has no logical network groups.");

        if (runtime.Shards.Any(item => item.Generation == runtime.Generation))
        {
            var existing = await ReserveExistingAssignmentAsync(ticketId, runtime, generationAssets, token);
            if (existing.Success && transaction is not null)
                await transaction.CommitAsync(token);
            return existing;
        }

        var groups = generationNetworks.GroupBy(item => item.PlacementGroupKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var key = group.Key;
                var assets = generationAssets.Where(item => item.PlacementGroupKey == key).ToArray();
                return new PlacementGroup(key, group.Any(item => item.IsEntry),
                    assets.Count(item => item.Kind == TeamLabResourceKind.Docker),
                    assets.Count(item => item.Kind == TeamLabResourceKind.Vm));
            })
            .OrderByDescending(item => item.IsEntry)
            .ThenByDescending(item => item.VmSlots)
            .ThenByDescending(item => item.DockerSlots)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        var candidates = await snapshots.LoadAsync(token);
        var assignment = Place(groups, candidates);
        if (assignment is null)
            return FleetCapacityReservationResult.Failed("No TeamLab-capable node set can host the logical network groups.");

        var shards = new Dictionary<Guid, TeamLabRuntimeShard>();
        foreach (var nodeId in assignment.Values.Distinct().Order())
        {
            var shard = new TeamLabRuntimeShard
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                WorkerNodeId = nodeId,
                Status = TeamLabRuntimeStatus.Pending
            };
            runtime.Shards.Add(shard);
            shards[nodeId] = shard;
        }

        foreach (var network in generationNetworks)
        {
            var nodeId = assignment[network.PlacementGroupKey];
            network.WorkerNodeId = nodeId;
            network.Shard = shards[nodeId];
        }
        foreach (var asset in generationAssets)
        {
            var nodeId = assignment[asset.PlacementGroupKey];
            asset.WorkerNodeId = nodeId;
            asset.Shard = shards[nodeId];
        }
        await context.SaveChangesAsync(token);

        var entryNetwork = generationNetworks.SingleOrDefault(item => item.IsEntry)
            ?? throw new InvalidOperationException("TeamLab runtime has no entry network.");
        runtime.EntryShardId = entryNetwork.ShardId;
        if (runtime.PublicUdpMapping is null)
            runtime.PublicUdpMapping = await AllocateUdpMappingAsync(runtime, entryNetwork.WorkerNodeId!.Value, token);
        else
            await RefreshUdpMappingAsync(runtime, entryNetwork.WorkerNodeId!.Value, token);

        var reservations = generationAssets.GroupBy(item => item.WorkerNodeId!.Value)
            .Select(group => new FleetCapacityReservation
            {
                DeploymentQueueTicketId = ticketId,
                WorkerNodeId = group.Key,
                DockerSlots = group.Count(item => item.Kind == TeamLabResourceKind.Docker),
                VmSlots = group.Count(item => item.Kind == TeamLabResourceKind.Vm),
                ExpiresAt = DateTimeOffset.UtcNow.Add(ReservationLifetime)
            }).ToArray();
        context.FleetCapacityReservations.AddRange(reservations);
        teamLabEvents.Record(
            runtime,
            "scheduling",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.PlacementSucceeded,
            OperationalEventOutcome.Succeeded,
            $"Physical placement assigned {groups.Length} network group(s) to {shards.Count} node shard(s).",
            detail: new Dictionary<string, object?>
            {
                ["generation"] = runtime.Generation,
                ["stage"] = "scheduling",
                ["shardCount"] = shards.Count,
                ["assetCount"] = generationAssets.Length
            });
        var ticket = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == ticketId, token);
        foreach (var reservation in reservations)
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Capacity.Reserved,
                OperationalEventOutcome.Succeeded,
                "Node capacity was reserved for a TeamLab shard.",
                workerNodeId: reservation.WorkerNodeId,
                detail: new Dictionary<string, object?>
                {
                    ["workload"] = ticket.Kind.ToString(),
                    ["operation"] = ticket.Operation.ToString(),
                    ["stage"] = ticket.Stage.ToString(),
                    ["dockerSlots"] = reservation.DockerSlots,
                    ["vmSlots"] = reservation.VmSlots,
                    ["generation"] = runtime.Generation
                }));
        await context.SaveChangesAsync(token);
        if (transaction is not null)
            await transaction.CommitAsync(token);

        var entryNodeId = entryNetwork.WorkerNodeId.Value;
        var entryNode = candidates.Single(item => item.Node.Id == entryNodeId).Node;
        var entryReservation = reservations.SingleOrDefault(item => item.WorkerNodeId == entryNodeId);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FleetCapacityReservationResult.Reserved(entryNode,
            entryReservation?.DockerSlots ?? 0, entryReservation?.VmSlots ?? 0);
    }

    async Task<FleetCapacityReservationResult> ReserveExistingAssignmentAsync(Guid ticketId,
        TeamLabRuntime runtime, IReadOnlyCollection<TeamLabRuntimeAsset> assets, CancellationToken token)
    {
        var items = assets.Where(item => item.WorkerNodeId != null)
            .GroupBy(item => item.WorkerNodeId!.Value)
            .Select(group => new FleetCapacityBatchItem(group.Key,
                group.Count(item => item.Kind == TeamLabResourceKind.Docker),
                group.Count(item => item.Kind == TeamLabResourceKind.Vm)))
            .ToArray();
        var nodeSnapshots = (await snapshots.LoadAsync(token)).ToDictionary(item => item.Node.Id);
        foreach (var item in items)
        {
            if (!nodeSnapshots.TryGetValue(item.NodeId, out var snapshot))
                return FleetCapacityReservationResult.Failed($"Assigned TeamLab node {item.NodeId} no longer exists.");
            var required = (item.DockerSlots > 0 ? NodeCapability.Docker : NodeCapability.None) |
                           (item.VmSlots > 0 ? NodeCapability.Kvm : NodeCapability.None);
            if (eligibility.GetReason(snapshot, required, item.DockerSlots, item.VmSlots, true,
                    [AgentFeatureIds.TeamLabFabric]) is { } reason)
                return FleetCapacityReservationResult.Failed($"Assigned TeamLab node is unavailable: {reason}.");
        }
        context.FleetCapacityReservations.AddRange(items.Select(item => new FleetCapacityReservation
        {
            DeploymentQueueTicketId = ticketId,
            WorkerNodeId = item.NodeId,
            DockerSlots = item.DockerSlots,
            VmSlots = item.VmSlots,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ReservationLifetime)
        }));
        var ticket = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == ticketId, token);
        foreach (var item in items)
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Capacity.Reserved,
                OperationalEventOutcome.Succeeded,
                "Node capacity was reserved for an existing TeamLab shard assignment.",
                workerNodeId: item.NodeId,
                detail: new Dictionary<string, object?>
                {
                    ["workload"] = ticket.Kind.ToString(),
                    ["operation"] = ticket.Operation.ToString(),
                    ["stage"] = ticket.Stage.ToString(),
                    ["dockerSlots"] = item.DockerSlots,
                    ["vmSlots"] = item.VmSlots,
                    ["generation"] = runtime.Generation
                }));
        await context.SaveChangesAsync(token);
        var entryNodeId = runtime.Shards.Single(item => item.Id == runtime.EntryShardId).WorkerNodeId;
        var entry = items.First(item => item.NodeId == entryNodeId);
        return FleetCapacityReservationResult.Reserved(nodeSnapshots[entryNodeId].Node,
            entry.DockerSlots, entry.VmSlots);
    }

    Dictionary<string, Guid>? Place(IReadOnlyList<PlacementGroup> groups,
        IReadOnlyList<NodeCapacitySnapshot> candidates)
    {
        var totalDocker = groups.Sum(item => item.DockerSlots);
        var totalVm = groups.Sum(item => item.VmSlots);
        var totalRequired = Required(totalDocker, totalVm);
        var single = candidates
            .Where(item => eligibility.GetReason(item, totalRequired, totalDocker, totalVm, true,
                [AgentFeatureIds.TeamLabFabric, AgentFeatureIds.WireGuard]) is null)
            .OrderByDescending(item => eligibility.Score(item, totalDocker, totalVm))
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Node.Id)
            .FirstOrDefault();
        if (single is not null)
            return groups.ToDictionary(item => item.Key, _ => single.Node.Id, StringComparer.Ordinal);

        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var allocated = new Dictionary<Guid, (int Docker, int Vm)>();
        foreach (var group in groups)
        {
            var selected = candidates.Select(item =>
                {
                    var used = allocated.GetValueOrDefault(item.Node.Id);
                    return new
                    {
                        Snapshot = item,
                        Docker = used.Docker + group.DockerSlots,
                        Vm = used.Vm + group.VmSlots,
                        Reused = allocated.ContainsKey(item.Node.Id)
                    };
                })
                .Where(item => eligibility.GetReason(item.Snapshot, Required(item.Docker, item.Vm),
                    item.Docker, item.Vm, true, group.IsEntry
                        ? [AgentFeatureIds.TeamLabFabric, AgentFeatureIds.WireGuard]
                        : [AgentFeatureIds.TeamLabFabric]) is null)
                .OrderByDescending(item => item.Reused)
                .ThenByDescending(item => eligibility.Score(item.Snapshot, item.Docker, item.Vm))
                .ThenBy(item => item.Snapshot.Node.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Snapshot.Node.Id)
                .FirstOrDefault();
            if (selected is null)
                return null;
            result[group.Key] = selected.Snapshot.Node.Id;
            allocated[selected.Snapshot.Node.Id] = (selected.Docker, selected.Vm);
        }
        return result;
    }

    async Task<TeamLabPublicUdpMapping> AllocateUdpMappingAsync(TeamLabRuntime runtime, Guid nodeId,
        CancellationToken token)
    {
        var node = await context.WorkerNodes.AsNoTracking().SingleAsync(item => item.Id == nodeId, token);
        if (string.IsNullOrWhiteSpace(node.TeamLabTunnelIp))
            throw new InvalidOperationException($"Node '{node.Name}' has no TeamLab tunnel IP.");
        var usedPublic = await context.TeamLabPublicUdpMappings.AsNoTracking()
            .Select(item => item.PublicUdpPort).ToArrayAsync(token);
        var usedWorker = await context.TeamLabPublicUdpMappings.AsNoTracking()
            .Where(item => item.WorkerTunnelIp == node.TeamLabTunnelIp)
            .Select(item => item.WorkerWireGuardPort).ToArrayAsync(token);
        return new TeamLabPublicUdpMapping
        {
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            PublicUdpPort = FirstFree(_network.PublicUdpPortStart, _network.PublicUdpPortEnd, usedPublic)
                ?? throw new InvalidOperationException("No TeamLab public UDP port is available."),
            WorkerTunnelIp = node.TeamLabTunnelIp,
            WorkerWireGuardPort = FirstFree(_network.WorkerWireGuardPortStart, _network.WorkerWireGuardPortEnd,
                usedWorker) ?? throw new InvalidOperationException("No Worker WireGuard UDP port is available."),
            RuleVersion = runtime.Generation
        };
    }

    async Task RefreshUdpMappingAsync(TeamLabRuntime runtime, Guid nodeId, CancellationToken token)
    {
        var mapping = runtime.PublicUdpMapping!;
        var node = await context.WorkerNodes.AsNoTracking().SingleAsync(item => item.Id == nodeId, token);
        if (string.IsNullOrWhiteSpace(node.TeamLabTunnelIp))
            throw new InvalidOperationException($"Node '{node.Name}' has no TeamLab tunnel IP.");
        var used = await context.TeamLabPublicUdpMappings.AsNoTracking()
            .Where(item => item.Id != mapping.Id && item.WorkerTunnelIp == node.TeamLabTunnelIp)
            .Select(item => item.WorkerWireGuardPort).ToArrayAsync(token);
        mapping.Generation = runtime.Generation;
        mapping.WorkerTunnelIp = node.TeamLabTunnelIp;
        mapping.WorkerWireGuardPort = FirstFree(_network.WorkerWireGuardPortStart,
            _network.WorkerWireGuardPortEnd, used)
            ?? throw new InvalidOperationException("No Worker WireGuard UDP port is available.");
        mapping.RuleVersion++;
        mapping.IsSynced = false;
        mapping.LastSyncError = null;
    }

    static NodeCapability Required(int docker, int vm) =>
        (docker > 0 ? NodeCapability.Docker : NodeCapability.None) |
        (vm > 0 ? NodeCapability.Kvm : NodeCapability.None);

    static int? FirstFree(int start, int end, IEnumerable<int> used)
    {
        var occupied = used.ToHashSet();
        for (var value = start; value <= end; value++)
            if (!occupied.Contains(value)) return value;
        return null;
    }

    sealed record PlacementGroup(string Key, bool IsEntry, int DockerSlots, int VmSlots);
}
