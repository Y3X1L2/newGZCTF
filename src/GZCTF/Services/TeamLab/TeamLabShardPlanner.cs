using GZCTF.Models.Data;
using GZCTF.Services.Fleet;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabShardPlan(
    Guid WorkerNodeId,
    string WorkerNodeName,
    string[] NetworkKeys,
    string[] AssetKeys,
    int DockerSlots,
    int VmSlots);

public sealed record TeamLabShardPlanResult(
    bool Success,
    string Message,
    IReadOnlyList<TeamLabShardPlan> Shards)
{
    public static TeamLabShardPlanResult Failed(string message) => new(false, message, []);
}

public static class TeamLabShardPlanner
{
    public static TeamLabShardPlanResult PlanShards(
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks,
        IReadOnlyList<TeamLabAssetSpec> assets,
        IReadOnlyList<WorkerNode> nodes)
    {
        if (networks.Count == 0)
            return TeamLabShardPlanResult.Failed("TeamLab topology has no network to place.");

        var candidates = nodes
            .Where(WeightedScheduler.CanHostTeamLabFabric)
            .OrderBy(NodeSortKey)
            .ToList();

        if (candidates.Count == 0)
            return TeamLabShardPlanResult.Failed("No TeamLab-capable WorkerNode is healthy.");

        var networkGroups = BuildNetworkGroups(networks, assets)
            .OrderByDescending(group => group.Networks.Any(IsEntryNetwork))
            .ThenByDescending(group => group.VmSlots)
            .ThenByDescending(group => group.DockerSlots)
            .ThenBy(group => group.SortKey, StringComparer.Ordinal)
            .ToArray();

        foreach (var group in networkGroups)
        {
            if (!candidates.Any(node => CanPlace(node, group.DockerSlots, group.VmSlots, 0, 0)))
                return TeamLabShardPlanResult.Failed(
                    $"The single network or connected network group {group.Name} exceeds every TeamLab WorkerNode capacity; split the network or increase node limits.");
        }

        var allDocker = networkGroups.Sum(group => group.DockerSlots);
        var allVm = networkGroups.Sum(group => group.VmSlots);
        var singleNode = candidates
            .Where(node => CanPlace(node, allDocker, allVm, 0, 0))
            .OrderByDescending(node => ScoreNode(node, allDocker, allVm))
            .ThenBy(NodeSortKey)
            .FirstOrDefault();

        if (singleNode is not null)
        {
            return new TeamLabShardPlanResult(true, "TeamLab shard plan built on one node.",
            [
                new TeamLabShardPlan(
                    singleNode.Id,
                    singleNode.Name,
                    networks.Select(n => n.TopologyKey).ToArray(),
                    assets.OrderBy(a => a.TopologyKey, StringComparer.Ordinal)
                        .Select(a => a.TopologyKey)
                        .ToArray(),
                    allDocker,
                    allVm)
            ]);
        }

        var placements = new Dictionary<Guid, MutableShardPlan>();
        foreach (var group in networkGroups)
        {
            var placed = candidates
                .Select(node =>
                {
                    placements.TryGetValue(node.Id, out var existing);
                    var reservedDocker = existing?.DockerSlots ?? 0;
                    var reservedVm = existing?.VmSlots ?? 0;
                    return new
                    {
                        Node = node,
                        Existing = existing,
                        CanPlace = CanPlace(node, group.DockerSlots, group.VmSlots, reservedDocker, reservedVm),
                        Score = ScoreNode(node, group.DockerSlots + reservedDocker, group.VmSlots + reservedVm)
                    };
                })
                .Where(item => item.CanPlace)
                .OrderByDescending(item => item.Existing is not null)
                .ThenByDescending(item => item.Score)
                .ThenBy(item => NodeSortKey(item.Node))
                .FirstOrDefault();

            if (placed is null)
                return TeamLabShardPlanResult.Failed("TeamLab multi-node capacity is insufficient for the published topology.");

            if (!placements.TryGetValue(placed.Node.Id, out var shard))
            {
                shard = new MutableShardPlan(placed.Node.Id, placed.Node.Name);
                placements.Add(placed.Node.Id, shard);
            }

            shard.NetworkKeys.AddRange(group.Networks.Select(network => network.TopologyKey));
            shard.AssetKeys.AddRange(group.Assets.Select(asset => asset.TopologyKey));
            shard.DockerSlots += group.DockerSlots;
            shard.VmSlots += group.VmSlots;
        }

        var shards = placements.Values
            .OrderBy(shard => shard.NetworkKeys.Contains("entry", StringComparer.Ordinal) ? 0 : 1)
            .ThenBy(shard => shard.WorkerNodeName, StringComparer.Ordinal)
            .ThenBy(shard => shard.WorkerNodeId)
            .Select(shard => new TeamLabShardPlan(
                shard.WorkerNodeId,
                shard.WorkerNodeName,
                shard.NetworkKeys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                shard.AssetKeys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                shard.DockerSlots,
                shard.VmSlots))
            .ToArray();

        return new TeamLabShardPlanResult(true, "TeamLab shard plan built.", shards);
    }

    static IReadOnlyList<NetworkGroup> BuildNetworkGroups(
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks,
        IReadOnlyList<TeamLabAssetSpec> assets)
    {
        var networkByKey = networks.ToDictionary(network => network.TopologyKey, StringComparer.Ordinal);
        var parent = networks.ToDictionary(network => network.TopologyKey, network => network.TopologyKey,
            StringComparer.Ordinal);

        foreach (var asset in assets)
        {
            var assetNetworkKeys = asset.Interfaces
                .Select(iface => iface.NetworkKey)
                .Where(key => networkByKey.ContainsKey(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            if (assetNetworkKeys.Length <= 1)
                continue;

            var first = assetNetworkKeys[0];
            foreach (var networkKey in assetNetworkKeys.Skip(1))
                Union(parent, first, networkKey);
        }

        return networks
            .GroupBy(network => Find(parent, network.TopologyKey), StringComparer.Ordinal)
            .Select(group =>
            {
                var groupNetworks = group
                    .OrderBy(network => network.TopologyKey, StringComparer.Ordinal)
                    .ToArray();
                var groupNetworkKeys = groupNetworks
                    .Select(network => network.TopologyKey)
                    .ToHashSet(StringComparer.Ordinal);
                var groupAssets = assets
                    .Where(asset => asset.Interfaces.Any(iface => groupNetworkKeys.Contains(iface.NetworkKey)))
                    .OrderBy(asset => asset.TopologyKey, StringComparer.Ordinal)
                    .ToArray();
                return new NetworkGroup(
                    groupNetworks,
                    groupAssets,
                    groupAssets.Count(asset => asset.Kind == TeamLabAssetSpecKind.Docker),
                    groupAssets.Count(asset => asset.Kind == TeamLabAssetSpecKind.Vm));
            })
            .ToArray();
    }

    static string Find(IDictionary<string, string> parent, string key)
    {
        if (!parent.TryGetValue(key, out var current))
            return key;

        if (string.Equals(current, key, StringComparison.Ordinal))
            return current;

        var root = Find(parent, current);
        parent[key] = root;
        return root;
    }

    static void Union(IDictionary<string, string> parent, string left, string right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (string.Equals(leftRoot, rightRoot, StringComparison.Ordinal))
            return;

        if (string.CompareOrdinal(leftRoot, rightRoot) > 0)
            (leftRoot, rightRoot) = (rightRoot, leftRoot);
        parent[rightRoot] = leftRoot;
    }

    static bool CanPlace(WorkerNode node, int dockerSlots, int vmSlots, int reservedDocker, int reservedVm)
    {
        if (WeightedScheduler.GetTeamLabAssetHostUnschedulableReason(
                node,
                requiresDocker: dockerSlots > 0,
                requiresVm: vmSlots > 0) is not null)
            return false;

        return node.AllocatedContainers + reservedDocker + dockerSlots <= node.MaxContainers &&
               node.AllocatedVms + reservedVm + vmSlots <= node.MaxVms;
    }

    static float ScoreNode(WorkerNode node, int dockerSlots, int vmSlots)
    {
        var projectedDocker = node.AllocatedContainers + dockerSlots;
        var projectedVm = node.AllocatedVms + vmSlots;
        return 1000f * (1 - Math.Clamp(node.CpuLoad, 0f, 1f)) +
               500f * (1 - Math.Clamp(node.MemoryLoad, 0f, 1f)) +
               250f * (1 - (float)projectedDocker / Math.Max(node.MaxContainers, 1)) +
               250f * (1 - (float)projectedVm / Math.Max(node.MaxVms, 1));
    }

    static string NodeSortKey(WorkerNode node) => $"{node.Name}\0{node.Id:D}";

    static bool IsEntryNetwork(TeamLabRuntimeNetworkSpec network) =>
        network.TopologyKey.Contains("entry", StringComparison.OrdinalIgnoreCase) ||
        network.Name.Contains("entry", StringComparison.OrdinalIgnoreCase) ||
        network.Name.Contains("入口", StringComparison.Ordinal);

    sealed record NetworkGroup(
        IReadOnlyList<TeamLabRuntimeNetworkSpec> Networks,
        IReadOnlyList<TeamLabAssetSpec> Assets,
        int DockerSlots,
        int VmSlots)
    {
        public string SortKey => string.Join(",", Networks.Select(network => network.TopologyKey));
        public string Name => string.Join(", ", Networks.Select(network => network.Name));
    }

    sealed class MutableShardPlan(Guid workerNodeId, string workerNodeName)
    {
        public Guid WorkerNodeId { get; } = workerNodeId;
        public string WorkerNodeName { get; } = workerNodeName;
        public List<string> NetworkKeys { get; } = [];
        public List<string> AssetKeys { get; } = [];
        public int DockerSlots { get; set; }
        public int VmSlots { get; set; }
    }
}
