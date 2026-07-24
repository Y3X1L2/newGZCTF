using System.Diagnostics;
using System.Text.Json;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
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
    TeamLabFabricLinkAllocator fabricLinks,
    IOptions<TeamLabNetworkConfig> options,
    IOptions<RuntimeSchedulingOptions> schedulingOptions)
{
    static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(30);
    readonly TeamLabNetworkConfig _network = options.Value;
    readonly RuntimeSchedulingOptions _scheduling = schedulingOptions.Value;

    public async Task<Guid?> SelectControlNodeAsync(CancellationToken token)
    {
        var candidates = await snapshots.LoadAsync(token);
        return candidates
            .Where(item => eligibility.GetReason(
                item, NodeCapability.None, dockerSlots: 0, vmSlots: 0, requireTeamLab: true) is null)
            .OrderByDescending(item => eligibility.Score(item, dockerSlots: 0, vmSlots: 0))
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Node.Id)
            .Select(item => (Guid?)item.Node.Id)
            .FirstOrDefault();
    }

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
            var existingRuntime = await context.TeamLabRuntimes
                .Include(item => item.Shards)
                .Include(item => item.Networks)
                .Include(item => item.Assets)
                .Include(item => item.Infrastructure).ThenInclude(item => item.Fragments)
                .Include(item => item.ObservationPoints)
                .Include(item => item.FabricLinkLeases)
                .SingleOrDefaultAsync(item => item.Id == runtimeId, token);
            if (existingRuntime is null)
                return FleetCapacityReservationResult.Failed("TeamLab runtime was not found.");
            if (existingRuntime.Shards.Any(item => item.Generation == existingRuntime.Generation))
            {
                await EnsurePlacementFactsAsync(
                    existingRuntime,
                    existingRuntime.Networks.Where(item => item.Generation == existingRuntime.Generation).ToArray(),
                    token);
                var runtimeEntryNode = existingRuntime.Shards
                    .Where(shard => shard.Id == existingRuntime.EntryShardId)
                    .Select(shard => (Guid?)shard.WorkerNodeId)
                    .FirstOrDefault();
                var selected = existingReservations.FirstOrDefault(item => item.WorkerNodeId == runtimeEntryNode) ??
                               existingReservations[0];
                var node = await context.WorkerNodes.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == selected.WorkerNodeId, token);
                return node is null
                    ? FleetCapacityReservationResult.Failed("Existing TeamLab reservation references a missing node.")
                    : FleetCapacityReservationResult.Reserved(node, ReservationVector(selected));
            }

            var reservationsToRebind = await context.FleetCapacityReservations
                .Where(item => item.DeploymentQueueTicketId == ticketId &&
                               item.Status == CapacityReservationStatus.Active)
                .ToArrayAsync(token);
            foreach (var reservation in reservationsToRebind)
            {
                reservation.Status = CapacityReservationStatus.Released;
                reservation.ReleasedAt = DateTimeOffset.UtcNow;
            }
            await context.SaveChangesAsync(token);
        }

        var runtime = await context.TeamLabRuntimes
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .Include(item => item.Infrastructure).ThenInclude(item => item.Fragments)
            .Include(item => item.ObservationPoints)
            .Include(item => item.FabricLinkLeases)
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
            await using var existingTransaction = context.Database.IsRelational()
                ? await context.Database.BeginTransactionAsync(token)
                : null;
            await EnsurePlacementFactsAsync(runtime, generationNetworks, token);
            var existing = await ReserveExistingAssignmentAsync(ticketId, runtime, generationAssets, token);
            if (existing.Success && existingTransaction is not null)
                await existingTransaction.CommitAsync(token);
            return existing;
        }

        var resourcesByAsset = await LoadAssetResourcesAsync(runtime.TopologyReleaseId, token);
        var edges = BuildPlacementEdges(runtime, generationNetworks);
        var groups = generationNetworks.GroupBy(item => item.PlacementGroupKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var key = group.Key;
                var assets = generationAssets.Where(item => item.PlacementGroupKey == key).ToArray();
                var resources = assets.Aggregate(WorkloadResourceVector.Zero, (sum, asset) =>
                    sum + RequiredResource(resourcesByAsset, asset.TopologyKey));
                return new PlacementGroup(key, group.Any(item => item.IsEntry),
                    resources,
                    assets.Any(item => item.Kind == TeamLabResourceKind.Docker &&
                                      item.EndpointObservation == TeamLabEndpointObservationMode.Required),
                    assets.Any(item => item.Kind == TeamLabResourceKind.Vm &&
                                      (!string.IsNullOrWhiteSpace(item.BootstrapDigest) ||
                                       item.EndpointObservation != TeamLabEndpointObservationMode.Disabled)));
            })
            .OrderByDescending(item => item.IsEntry)
            .ThenByDescending(item => edges.Where(edge => edge.Touches(item.Key)).Sum(edge => edge.Weight))
            .ThenByDescending(item => item.Resources.MemoryMiB)
            .ThenByDescending(item => item.Resources.CpuUnits)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        var candidates = ApplyCompletedGenerationCredits(runtime, await snapshots.LoadAsync(token));
        var placementTimer = Stopwatch.StartNew();
        var improvementPasses = 0;
        var assignment = TryReusePreviousGenerationPlacement(runtime, groups, candidates);
        var reusedPreviousPlacement = assignment is not null;
        assignment ??= Place(groups, edges, candidates, out improvementPasses);
        placementTimer.Stop();
        activity?.SetTag("teamlab.placement.elapsed_ms", placementTimer.ElapsedMilliseconds);
        activity?.SetTag("teamlab.placement.group_count", groups.Length);
        activity?.SetTag("teamlab.placement.edge_count", edges.Count);
        activity?.SetTag("teamlab.placement.improvement_passes", improvementPasses);
        if (assignment is null)
        {
            var oversized = groups.FirstOrDefault(group => !CanAnyNodeHost(group, candidates));
            return oversized is null
                ? FleetCapacityReservationResult.Failed(
                    "No TeamLab-capable node set can host the logical network groups.")
                : FleetCapacityReservationResult.Failed(
                    $"single_network_capacity_exceeded: placement group '{oversized.Key}' exceeds every eligible node.");
        }

        var revalidationCandidates = ApplyCompletedGenerationCredits(runtime, await snapshots.LoadAsync(token));
        if (!RevalidateAssignment(
                assignment, groups, revalidationCandidates, ignoreDynamicLoad: reusedPreviousPlacement))
            return FleetCapacityReservationResult.Failed(
                "capacity_changed: node capacity changed before the placement could be reserved.");
        candidates = revalidationCandidates;

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(token)
            : null;

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
        await EnsurePlacementFactsAsync(runtime, generationNetworks, token);

        var entryNetwork = generationNetworks.SingleOrDefault(item => item.IsEntry)
            ?? throw new InvalidOperationException("TeamLab runtime has no entry network.");
        runtime.EntryShardId = entryNetwork.ShardId;
        if (!runtime.IsScenarioBuild)
        {
            if (runtime.PublicUdpMapping is null)
                runtime.PublicUdpMapping = await AllocateUdpMappingAsync(runtime, entryNetwork.WorkerNodeId!.Value, token);
            else
                await RefreshUdpMappingAsync(runtime, entryNetwork.WorkerNodeId!.Value, token);
        }

        var reservations = generationAssets.GroupBy(item => item.WorkerNodeId!.Value)
            .Select(group =>
            {
                var resources = group.Aggregate(WorkloadResourceVector.Zero,
                    (sum, asset) => sum + RequiredResource(resourcesByAsset, asset.TopologyKey));
                return new FleetCapacityReservation
                {
                    DeploymentQueueTicketId = ticketId,
                    WorkerNodeId = group.Key,
                    CpuUnits = resources.CpuUnits,
                    MemoryMiB = resources.MemoryMiB,
                    StorageMiB = resources.StorageMiB,
                    DockerSlots = resources.DockerSlots,
                    VmSlots = resources.VmSlots,
                    ExpiresAt = DateTimeOffset.UtcNow.Add(ReservationLifetime)
                };
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
                ["assetCount"] = generationAssets.Length,
                ["placementElapsedMs"] = placementTimer.ElapsedMilliseconds,
                ["placementGroupCount"] = groups.Length,
                ["placementEdgeCount"] = edges.Count,
                ["placementImprovementPasses"] = improvementPasses
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
                    ["cpuUnits"] = reservation.CpuUnits,
                    ["memoryMiB"] = reservation.MemoryMiB,
                    ["storageMiB"] = reservation.StorageMiB,
                    ["dockerSlots"] = reservation.DockerSlots,
                    ["vmSlots"] = reservation.VmSlots,
                    ["generation"] = runtime.Generation
                }));
        await context.SaveChangesAsync(token);
        if (transaction is not null)
            await transaction.CommitAsync(token);

        var entryNodeId = entryNetwork.WorkerNodeId.GetValueOrDefault();
        var entryNode = candidates.Single(item => item.Node.Id == entryNodeId).Node;
        var entryReservation = reservations.SingleOrDefault(item => item.WorkerNodeId == entryNodeId);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FleetCapacityReservationResult.Reserved(entryNode,
            entryReservation is null
                ? WorkloadResourceVector.Zero
                : ReservationVector(entryReservation));
    }

    async Task<FleetCapacityReservationResult> ReserveExistingAssignmentAsync(Guid ticketId,
        TeamLabRuntime runtime, IReadOnlyCollection<TeamLabRuntimeAsset> assets, CancellationToken token)
    {
        var resourcesByAsset = await LoadAssetResourcesAsync(runtime.TopologyReleaseId, token);
        var items = assets.Where(item => item.WorkerNodeId != null)
            .GroupBy(item => item.WorkerNodeId!.Value)
            .Select(group => new FleetCapacityBatchItem(group.Key,
                group.Aggregate(WorkloadResourceVector.Zero,
                    (sum, asset) => sum + RequiredResource(resourcesByAsset, asset.TopologyKey))))
            .ToArray();
        var nodeSnapshots = (await snapshots.LoadAsync(token)).ToDictionary(item => item.Node.Id);
        foreach (var item in items)
        {
            if (!nodeSnapshots.TryGetValue(item.NodeId, out var snapshot))
                return FleetCapacityReservationResult.Failed($"Assigned TeamLab node {item.NodeId} no longer exists.");
            var required = (item.DockerSlots > 0 ? NodeCapability.Docker : NodeCapability.None) |
                           (item.VmSlots > 0 ? NodeCapability.Kvm : NodeCapability.None);
            if (eligibility.GetReason(snapshot, required, item.Resources, true) is { } reason)
                return FleetCapacityReservationResult.Failed($"Assigned TeamLab node is unavailable: {reason}.");
        }
        context.FleetCapacityReservations.AddRange(items.Select(item => new FleetCapacityReservation
        {
            DeploymentQueueTicketId = ticketId,
            WorkerNodeId = item.NodeId,
            CpuUnits = item.Resources.CpuUnits,
            MemoryMiB = item.Resources.MemoryMiB,
            StorageMiB = item.Resources.StorageMiB,
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
                    ["cpuUnits"] = item.Resources.CpuUnits,
                    ["memoryMiB"] = item.Resources.MemoryMiB,
                    ["storageMiB"] = item.Resources.StorageMiB,
                    ["dockerSlots"] = item.DockerSlots,
                    ["vmSlots"] = item.VmSlots,
                    ["generation"] = runtime.Generation
                }));
        await context.SaveChangesAsync(token);
        var entryNodeId = runtime.Shards.Single(item => item.Id == runtime.EntryShardId).WorkerNodeId;
        var entry = items.First(item => item.NodeId == entryNodeId);
        return FleetCapacityReservationResult.Reserved(nodeSnapshots[entryNodeId].Node, entry.Resources);
    }

    internal static IReadOnlyList<NodeCapacitySnapshot> ApplyCompletedGenerationCredits(
        TeamLabRuntime runtime,
        IReadOnlyList<NodeCapacitySnapshot> snapshots)
    {
        if (runtime.Generation <= 1) return snapshots;
        var credits = runtime.Assets
            .Where(item => item.Generation == runtime.Generation - 1 &&
                           item.Status == TeamLabRuntimeStatus.Destroyed &&
                           item.WorkerNodeId != null &&
                           item.ExecutionUpdatedAt != null)
            .GroupBy(item => item.WorkerNodeId!.Value)
            .ToDictionary(group => group.Key, group => new
            {
                Docker = group.Count(item => item.Kind == TeamLabResourceKind.Docker),
                Vm = group.Count(item => item.Kind == TeamLabResourceKind.Vm),
                CleanupCompletedAt = group.Max(item => item.ExecutionUpdatedAt!.Value)
            });
        if (credits.Count == 0) return snapshots;

        return snapshots.Select(snapshot =>
        {
            if (!credits.TryGetValue(snapshot.Node.Id, out var credit) ||
                snapshot.Node.LastHeartbeat >= credit.CleanupCompletedAt)
                return snapshot;
            return snapshot with
            {
                LiveDocker = Math.Max(0, snapshot.LiveDocker - credit.Docker),
                LiveVm = Math.Max(0, snapshot.LiveVm - credit.Vm)
            };
        }).ToArray();
    }

    Dictionary<string, Guid>? TryReusePreviousGenerationPlacement(
        TeamLabRuntime runtime,
        IReadOnlyList<PlacementGroup> groups,
        IReadOnlyList<NodeCapacitySnapshot> candidates)
    {
        if (runtime.Generation <= 1) return null;

        var previousGeneration = runtime.Generation - 1;
        var previousNetworks = runtime.Networks
            .Where(item => item.Generation == previousGeneration && item.WorkerNodeId != null)
            .GroupBy(item => item.PlacementGroupKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (previousNetworks.Count != groups.Count) return null;

        var previousAssets = runtime.Assets.Where(item => item.Generation == previousGeneration)
            .GroupBy(item => item.PlacementGroupKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var currentNetworkCounts = runtime.Networks.Where(item => item.Generation == runtime.Generation)
            .GroupBy(item => item.PlacementGroupKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var assignment = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            if (!previousNetworks.TryGetValue(group.Key, out var networks) ||
                networks.Length != currentNetworkCounts.GetValueOrDefault(group.Key) ||
                networks.Select(item => item.WorkerNodeId!.Value).Distinct().Count() != 1)
                return null;
            var assets = previousAssets.GetValueOrDefault(group.Key) ?? [];
            if (assets.Count(item => item.Kind == TeamLabResourceKind.Docker) != group.DockerSlots ||
                assets.Count(item => item.Kind == TeamLabResourceKind.Vm) != group.VmSlots)
                return null;
            assignment[group.Key] = networks[0].WorkerNodeId!.Value;
        }

        var snapshotsByNode = candidates.ToDictionary(item => item.Node.Id);
        foreach (var nodeGroups in groups.GroupBy(group => assignment[group.Key]))
        {
            if (!snapshotsByNode.TryGetValue(nodeGroups.Key, out var snapshot)) return null;
            var resources = nodeGroups.Aggregate(WorkloadResourceVector.Zero,
                (sum, item) => sum + item.Resources);
            var features = nodeGroups.SelectMany(group => RequiredFeatures(group) ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (eligibility.GetReason(snapshot,
                    Required(resources.DockerSlots, resources.VmSlots), resources,
                    requireTeamLab: true, requiredFeatures: features, ignoreDynamicLoad: true) is not null)
                return null;
        }

        return assignment;
    }

    async Task EnsurePlacementFactsAsync(
        TeamLabRuntime runtime,
        IReadOnlyList<TeamLabRuntimeNetwork> generationNetworks,
        CancellationToken token)
    {
        var shards = runtime.Shards.Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.WorkerNodeId).ThenBy(item => item.Id).ToArray();
        var networkByKey = generationNetworks.ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
        foreach (var infrastructure in runtime.Infrastructure
                     .Where(item => item.Generation == runtime.Generation)
                     .OrderBy(item => item.TopologyKey, StringComparer.Ordinal))
        {
            var interfaces = Deserialize<TeamLabRuntimeInfrastructureInterfaceIntent>(
                infrastructure.InterfaceSummaryJson);
            var shardInterfaces = infrastructure.Kind == TeamLabInfrastructureKind.ManagedSwitch
                ? SwitchFragments(infrastructure, networkByKey)
                : interfaces.Where(item => networkByKey.ContainsKey(item.NetworkKey))
                    .GroupBy(item => networkByKey[item.NetworkKey].ShardId!.Value)
                    .ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var (shardId, fragmentInterfaces) in shardInterfaces.OrderBy(item => item.Key))
            {
                if (infrastructure.Fragments.Any(item => item.ShardId == shardId)) continue;
                var shard = shards.Single(item => item.Id == shardId);
                infrastructure.Fragments.Add(new TeamLabRuntimeInfrastructureFragment
                {
                    Shard = shard,
                    ShardId = shard.Id,
                    WorkerNodeId = shard.WorkerNodeId,
                    FragmentKey = $"{infrastructure.TopologyKey}:{shard.PublicId:N}",
                    InterfaceSummaryJson = JsonSerializer.Serialize(fragmentInterfaces),
                    Status = TeamLabRuntimeStatus.Pending
                });
            }
        }
        await context.SaveChangesAsync(token);

        var existingLeaseShards = runtime.FabricLinkLeases
            .Where(item => item.Generation == runtime.Generation && item.ReleasedAt == null)
            .Select(item => item.ShardId)
            .ToHashSet();
        var leases = await fabricLinks.AllocateAsync(runtime, shards, token);
        var allocatedLeaseCount = leases.Count(item => !existingLeaseShards.Contains(item.ShardId));
        if (allocatedLeaseCount > 0)
        {
            teamLabEvents.Record(
                runtime,
                "fabric",
                TeamLabEventLevel.Success,
                OperationalEventCodes.TeamLab.FabricLeaseAllocated,
                OperationalEventOutcome.Succeeded,
                "Fabric link leases were allocated for runtime shards.",
                detail: new Dictionary<string, object?>
                {
                    ["generation"] = runtime.Generation,
                    ["stage"] = "fabric",
                    ["leaseCount"] = allocatedLeaseCount,
                    ["shardCount"] = shards.Length
                });
            PlatformTelemetry.RecordTeamLabInfrastructure("allocated", "fabric-link");
        }
        await context.SaveChangesAsync(token);
        foreach (var network in generationNetworks)
        {
            if (runtime.ObservationPoints.Any(item => item.Generation == runtime.Generation &&
                                                      item.Kind == TeamLabObservationPointKind.NetworkBridge &&
                                                      item.NetworkId == network.Id))
                continue;
            runtime.ObservationPoints.Add(new TeamLabObservationPoint
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                WorkerNodeId = network.WorkerNodeId!.Value,
                ShardId = network.ShardId,
                NetworkId = network.Id,
                Kind = TeamLabObservationPointKind.NetworkBridge,
                TopologyKey = network.TopologyKey,
                InterfaceToken = network.BridgeName
            });
        }
        foreach (var fragment in runtime.Infrastructure
                     .Where(item => item.Generation == runtime.Generation)
                     .Where(item => item.Kind == TeamLabInfrastructureKind.ManagedRouter)
                     .SelectMany(item => item.Fragments))
        {
            if (runtime.ObservationPoints.Any(item => item.Generation == runtime.Generation &&
                                                      item.Kind == TeamLabObservationPointKind.RouterFragment &&
                                                      item.InfrastructureFragmentId == fragment.Id))
                continue;
            runtime.ObservationPoints.Add(new TeamLabObservationPoint
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                WorkerNodeId = fragment.WorkerNodeId,
                ShardId = fragment.ShardId,
                InfrastructureFragmentId = fragment.Id,
                Kind = TeamLabObservationPointKind.RouterFragment,
                TopologyKey = fragment.Infrastructure.TopologyKey,
                InterfaceToken = TeamLabResourceNameFactory.RouterNamespace(runtime.Id, fragment.ShardId)
            });
        }
        foreach (var fabric in leases)
        {
            if (runtime.ObservationPoints.Any(item => item.Generation == runtime.Generation &&
                                                      item.Kind == TeamLabObservationPointKind.FabricUplink &&
                                                      item.ShardId == fabric.ShardId))
                continue;
            runtime.ObservationPoints.Add(new TeamLabObservationPoint
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                WorkerNodeId = fabric.WorkerNodeId,
                ShardId = fabric.ShardId,
                Kind = TeamLabObservationPointKind.FabricUplink,
                TopologyKey = $"fabric-{fabric.ShardId}",
                InterfaceToken = TeamLabResourceNameFactory.FabricHostInterface(runtime.Id)
            });
        }
        var networksByKey = generationNetworks.ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
        foreach (var asset in runtime.Assets.Where(item =>
                     item.Generation == runtime.Generation &&
                     item.EndpointObservation != TeamLabEndpointObservationMode.Disabled &&
                     item.WorkerNodeId != null && item.ShardId != null))
        {
            var interfaces = JsonSerializer.Deserialize<WorkloadInterfaceIntent[]>(asset.InterfaceSummaryJson) ?? [];
            foreach (var iface in interfaces)
            {
                var interfaceToken = TeamLabResourceNameFactory.WorkloadHostInterface(
                    runtime.Id, asset.TopologyKey, iface.Key);
                if (runtime.ObservationPoints.Any(item => item.Generation == runtime.Generation &&
                                                          item.Kind == TeamLabObservationPointKind.WorkloadEndpoint &&
                                                          item.AssetId == asset.Id &&
                                                          item.InterfaceToken == interfaceToken))
                    continue;
                runtime.ObservationPoints.Add(new TeamLabObservationPoint
                {
                    RuntimeId = runtime.Id,
                    Generation = runtime.Generation,
                    WorkerNodeId = asset.WorkerNodeId.GetValueOrDefault(),
                    ShardId = asset.ShardId,
                    NetworkId = networksByKey.GetValueOrDefault(iface.NetworkKey)?.Id,
                    AssetId = asset.Id,
                    Kind = TeamLabObservationPointKind.WorkloadEndpoint,
                    TopologyKey = asset.TopologyKey,
                    InterfaceToken = interfaceToken
                });
            }
        }
        await context.SaveChangesAsync(token);
    }

    static Dictionary<int, TeamLabRuntimeInfrastructureInterfaceIntent[]> SwitchFragments(
        TeamLabRuntimeInfrastructure infrastructure,
        IReadOnlyDictionary<string, TeamLabRuntimeNetwork> networkByKey)
    {
        if (infrastructure.NetworkKey is null || !networkByKey.TryGetValue(infrastructure.NetworkKey, out var network) ||
            network.ShardId is null)
            throw new TeamLabRuntimeExecutionException(
                $"Managed switch '{infrastructure.TopologyKey}' has no placed runtime network.");
        return new Dictionary<int, TeamLabRuntimeInfrastructureInterfaceIntent[]>
        {
            [network.ShardId.Value] = []
        };
    }

    async Task<IReadOnlyDictionary<string, WorkloadResourceVector>> LoadAssetResourcesAsync(
        Guid releaseId,
        CancellationToken token)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releaseId, token)
            ?? throw new TeamLabRuntimeExecutionException(
                $"Topology release '{releaseId:D}' no longer exists.");
        var topology = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        return topology.Assets.Where(item => item.IsImageBacked)
            .ToDictionary(
                item => item.Key,
                item => new WorkloadResourceVector(
                    item.CpuUnits,
                    item.MemoryMiB,
                    item.StorageMiB,
                    item.Kind == TeamLabAssetKind.Docker ? 1 : 0,
                    item.Kind == TeamLabAssetKind.Vm ? 1 : 0),
                StringComparer.Ordinal);
    }

    static WorkloadResourceVector RequiredResource(
        IReadOnlyDictionary<string, WorkloadResourceVector> resources,
        string assetKey) =>
        resources.TryGetValue(assetKey, out var value)
            ? value
            : throw new TeamLabRuntimeExecutionException(
                $"Runtime asset '{assetKey}' has no release resource contract.");

    static WorkloadResourceVector ReservationVector(FleetCapacityReservation reservation) =>
        new(
            reservation.CpuUnits,
            reservation.MemoryMiB,
            reservation.StorageMiB,
            reservation.DockerSlots,
            reservation.VmSlots);

    static IReadOnlyList<PlacementEdge> BuildPlacementEdges(
        TeamLabRuntime runtime,
        IReadOnlyList<TeamLabRuntimeNetwork> networks)
    {
        var groupByNetwork = networks.ToDictionary(
            item => item.TopologyKey, item => item.PlacementGroupKey, StringComparer.Ordinal);
        return runtime.Infrastructure.Where(item => item.Generation == runtime.Generation)
            .SelectMany(item => Deserialize<TeamLabRuntimeInfrastructureConnectionIntent>(item.ConnectionSummaryJson))
            .Where(item => groupByNetwork.ContainsKey(item.FromNetworkKey) &&
                           groupByNetwork.ContainsKey(item.ToNetworkKey) &&
                           groupByNetwork[item.FromNetworkKey] != groupByNetwork[item.ToNetworkKey])
            .Select(item => new PlacementEdge(
                groupByNetwork[item.FromNetworkKey],
                groupByNetwork[item.ToNetworkKey],
                item.Direction == TeamLabConnectionDirection.Bidirectional ? 2 : 1))
            .OrderBy(item => item.Left, StringComparer.Ordinal)
            .ThenBy(item => item.Right, StringComparer.Ordinal)
            .ToArray();
    }

    static T[] Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T[]>(json) ?? [];
        }
        catch (JsonException exception)
        {
            throw new TeamLabRuntimeExecutionException(
                $"Runtime infrastructure facts are invalid: {exception.Message}");
        }
    }

    Dictionary<string, Guid>? Place(
        IReadOnlyList<PlacementGroup> groups,
        IReadOnlyList<PlacementEdge> edges,
        IReadOnlyList<NodeCapacitySnapshot> candidates,
        out int improvementPasses)
    {
        improvementPasses = 0;
        var total = groups.Aggregate(WorkloadResourceVector.Zero, (sum, item) => sum + item.Resources);
        var totalRequired = Required(total.DockerSlots, total.VmSlots);
        var singleFeatures = groups.SelectMany(group => RequiredFeatures(group) ?? [])
            .Append(AgentFeatureIds.WireGuard)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var single = candidates
            .Where(item => eligibility.GetReason(item, totalRequired, total, true,
                singleFeatures) is null)
            .OrderByDescending(item => eligibility.Score(item, total))
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Node.Id)
            .FirstOrDefault();
        if (single is not null)
            return groups.ToDictionary(item => item.Key, _ => single.Node.Id, StringComparer.Ordinal);

        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var allocated = new Dictionary<Guid, WorkloadResourceVector>();
        foreach (var group in groups)
        {
            var selected = candidates.Select(item =>
                {
                    var used = allocated.GetValueOrDefault(item.Node.Id);
                    var requested = used + group.Resources;
                    return new
                    {
                        Snapshot = item,
                        Requested = requested,
                        Reused = allocated.ContainsKey(item.Node.Id),
                        CrossNodeEdges = CrossNodeCost(group.Key, item.Node.Id, result, edges)
                    };
                })
                .Where(item => eligibility.GetReason(item.Snapshot,
                    Required(item.Requested.DockerSlots, item.Requested.VmSlots),
                    item.Requested, true, RequiredFeatures(group)) is null)
                .OrderBy(item => item.CrossNodeEdges)
                .ThenByDescending(item => item.Reused)
                .ThenByDescending(item => eligibility.Score(item.Snapshot, item.Requested))
                .ThenBy(item => item.Snapshot.Node.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Snapshot.Node.Id)
                .FirstOrDefault();
            if (selected is null)
                return null;
            result[group.Key] = selected.Snapshot.Node.Id;
            allocated[selected.Snapshot.Node.Id] = selected.Requested;
        }
        improvementPasses = ImprovePlacement(result, groups, edges, candidates);
        return result;
    }

    int ImprovePlacement(
        Dictionary<string, Guid> assignment,
        IReadOnlyList<PlacementGroup> groups,
        IReadOnlyList<PlacementEdge> edges,
        IReadOnlyList<NodeCapacitySnapshot> candidates)
    {
        var timer = Stopwatch.StartNew();
        var groupByKey = groups.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var allocated = assignment.GroupBy(item => item.Value)
            .ToDictionary(group => group.Key,
                group => group.Aggregate(WorkloadResourceVector.Zero,
                    (sum, item) => sum + groupByKey[item.Key].Resources));
        var completedPasses = 0;
        var maximumPasses = Math.Max(0, _scheduling.PlacementImprovementPasses);
        var budget = TimeSpan.FromMilliseconds(Math.Max(1, _scheduling.PlacementComputationBudgetMs));
        for (var pass = 0; pass < maximumPasses && timer.Elapsed < budget; pass++)
        {
            completedPasses++;
            var changed = false;
            foreach (var group in groups.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (timer.Elapsed >= budget) break;
                var currentNodeId = assignment[group.Key];
                var currentCost = TotalCrossNodeCost(assignment, edges);
                var selected = candidates
                    .Where(item => item.Node.Id != currentNodeId)
                    .Select(item =>
                    {
                        var used = allocated.GetValueOrDefault(item.Node.Id);
                        var requested = used + group.Resources;
                        return new
                        {
                            Snapshot = item,
                            Requested = requested,
                            Cost = TotalCrossNodeCost(assignment, edges, group.Key, item.Node.Id)
                        };
                    })
                    .Where(item => item.Cost < currentCost &&
                                   eligibility.GetReason(item.Snapshot,
                                       Required(item.Requested.DockerSlots, item.Requested.VmSlots),
                                       item.Requested, true, RequiredFeatures(group)) is null)
                    .OrderBy(item => item.Cost)
                    .ThenByDescending(item => eligibility.Score(item.Snapshot, item.Requested))
                    .ThenBy(item => item.Snapshot.Node.Name, StringComparer.Ordinal)
                    .ThenBy(item => item.Snapshot.Node.Id)
                    .FirstOrDefault();
                if (selected is null) continue;
                var current = allocated[currentNodeId];
                allocated[currentNodeId] = current - group.Resources;
                allocated[selected.Snapshot.Node.Id] = selected.Requested;
                assignment[group.Key] = selected.Snapshot.Node.Id;
                changed = true;
            }
            if (!changed) break;
        }
        return completedPasses;
    }

    bool CanAnyNodeHost(PlacementGroup group, IReadOnlyList<NodeCapacitySnapshot> candidates) =>
        candidates.Any(candidate => eligibility.GetReason(
            candidate,
            Required(group.DockerSlots, group.VmSlots),
            group.Resources,
            requireTeamLab: true,
            requiredFeatures: RequiredFeatures(group),
            ignoreDynamicLoad: true) is null);

    bool RevalidateAssignment(
        IReadOnlyDictionary<string, Guid> assignment,
        IReadOnlyList<PlacementGroup> groups,
        IReadOnlyList<NodeCapacitySnapshot> candidates,
        bool ignoreDynamicLoad = false)
    {
        var snapshotsByNode = candidates.ToDictionary(item => item.Node.Id);
        foreach (var nodeGroups in groups.GroupBy(group => assignment[group.Key]))
        {
            if (!snapshotsByNode.TryGetValue(nodeGroups.Key, out var snapshot)) return false;
            var resources = nodeGroups.Aggregate(WorkloadResourceVector.Zero,
                (sum, group) => sum + group.Resources);
            var requiredFeatures = nodeGroups.SelectMany(group => RequiredFeatures(group) ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (eligibility.GetReason(
                    snapshot,
                    Required(resources.DockerSlots, resources.VmSlots),
                    resources,
                    requireTeamLab: true,
                    requiredFeatures: requiredFeatures,
                    ignoreDynamicLoad: ignoreDynamicLoad) is not null)
                return false;
        }
        return true;
    }

    static int CrossNodeCost(
        string groupKey,
        Guid candidateNodeId,
        IReadOnlyDictionary<string, Guid> assignment,
        IReadOnlyList<PlacementEdge> edges) =>
        edges.Where(edge => edge.Touches(groupKey))
            .Sum(edge => assignment.TryGetValue(edge.Other(groupKey), out var otherNodeId) &&
                         otherNodeId != candidateNodeId
                ? edge.Weight
                : 0);

    static int TotalCrossNodeCost(
        IReadOnlyDictionary<string, Guid> assignment,
        IReadOnlyList<PlacementEdge> edges,
        string? overrideGroup = null,
        Guid? overrideNode = null) =>
        edges.Sum(edge =>
        {
            var left = edge.Left == overrideGroup ? overrideNode : assignment.GetValueOrDefault(edge.Left);
            var right = edge.Right == overrideGroup ? overrideNode : assignment.GetValueOrDefault(edge.Right);
            return left.HasValue && right.HasValue && left != right ? edge.Weight : 0;
        });

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

    static IReadOnlyCollection<string>? RequiredFeatures(PlacementGroup group)
    {
        var features = new List<string>();
        if (group.IsEntry) features.Add(AgentFeatureIds.WireGuard);
        if (group.DockerEndpointSensorRequired) features.Add(AgentFeatureIds.TeamLabEndpointSensor);
        if (group.ManagedVmRequired)
        {
            features.Add(AgentFeatureIds.VmGuestManagement);
            features.Add(AgentFeatureIds.VmConfigDriveV2);
            features.Add(AgentFeatureIds.VmPreparedImage);
            features.Add(AgentFeatureIds.RuntimeSignals);
        }
        return features.Count == 0 ? null : features;
    }

    sealed record PlacementGroup(
        string Key,
        bool IsEntry,
        WorkloadResourceVector Resources,
        bool DockerEndpointSensorRequired,
        bool ManagedVmRequired)
    {
        public int DockerSlots => Resources.DockerSlots;
        public int VmSlots => Resources.VmSlots;
    }

    sealed record WorkloadInterfaceIntent(
        string Key,
        string NetworkKey,
        string IpAddress,
        int PrefixLength,
        string MacAddress,
        bool Primary);

    sealed record PlacementEdge(string Left, string Right, int Weight)
    {
        public bool Touches(string key) => Left == key || Right == key;
        public string Other(string key) => Left == key ? Right : Left;
    }
}
