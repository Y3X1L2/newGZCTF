using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.Runtime.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabPlanningNodeSnapshot(
    Guid Id,
    string Name,
    bool SupportsDocker,
    bool SupportsVm,
    int AvailableDockerSlots,
    int AvailableVmSlots,
    float CpuLoad,
    float MemoryLoad,
    WorkloadResourceVector AvailableResources = default);

public static class TeamLabAssetPlanner
{
    public static TeamLabPlanModel Build(
        Guid topologyId,
        Guid releaseId,
        TeamLabExecutionTopology definition,
        IReadOnlyList<TeamLabPlanningNodeSnapshot> nodes)
    {
        var networks = definition.Networks
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new TeamLabPlanNetworkModel(
                item.Key,
                item.Name,
                FirstSubnet(item.AddressPoolCidr, item.RuntimePrefixLength),
                item.IsEntry))
            .ToArray();
        var assets = definition.Assets
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new TeamLabPlanAssetModel(
                item.Key,
                item.Name,
                item.Kind,
                item.ImageTemplateId,
                new TeamLabAssetResourceModel(item.CpuUnits, item.MemoryMiB, item.StorageMiB),
                item.Interfaces.Select(iface => new TeamLabPlanInterfaceModel(
                    iface.Key, iface.NetworkKey, iface.HostOffset, iface.Primary)).ToArray(),
                item.RoutingEnabled,
                item.ImageDigest))
            .ToArray();

        var rawGroups = BuildGroups(definition);
        var edges = BuildPlacementEdges(definition, rawGroups);
        var groups = rawGroups.OrderByDescending(group => group.IsEntry)
            .ThenByDescending(group => edges.Where(edge => edge.Touches(group.Key)).Sum(edge => edge.Weight))
            .ThenByDescending(group => group.VmSlots)
            .ThenByDescending(group => group.DockerSlots)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var placements = Place(groups, edges, nodes);
        if (placements is null)
            throw new TeamLabApiContractException(
                "capability_unavailable",
                "当前 TeamLab 节点集合无法放置该拓扑",
                409);

        var shards = placements
            .OrderByDescending(item => item.Groups.Any(group => group.IsEntry))
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Node.Id)
            .Select((item, index) => new TeamLabPlanShardModel(
                $"shard-{index + 1}",
                item.Groups.SelectMany(group => group.NetworkKeys).Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                item.Groups.SelectMany(group => group.AssetKeys).Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                item.Groups.Sum(group => group.DockerSlots),
                item.Groups.Sum(group => group.VmSlots)))
            .ToArray();
        var shardByNetwork = shards.SelectMany(shard => shard.NetworkKeys.Select(key => (key, shard.Key)))
            .ToDictionary(item => item.key, item => item.Key, StringComparer.Ordinal);
        shards = shards.Select(shard => shard with
        {
            InfrastructureKeys = definition.Infrastructure
                .Where(item => item.Kind == TeamLabInfrastructureKind.ManagedSwitch
                    ? item.NetworkKey is not null && shard.NetworkKeys.Contains(item.NetworkKey, StringComparer.Ordinal)
                    : item.Interfaces.Any(iface => shard.NetworkKeys.Contains(iface.NetworkKey, StringComparer.Ordinal)))
                .Select(item => item.Key)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray()
        }).ToArray();
        var crossShardConnections = definition.Connections.Count(connection =>
            shardByNetwork.GetValueOrDefault(connection.FromNetworkKey) !=
            shardByNetwork.GetValueOrDefault(connection.ToNetworkKey));
        var capabilities = new List<string> { "teamlab-fabric" };
        if (assets.Any(item => item.Kind == TeamLabAssetKind.Docker)) capabilities.Add("docker");
        if (assets.Any(item => item.Kind == TeamLabAssetKind.Vm)) capabilities.Add("kvm");

        var hashPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            topologyId,
            releaseId,
            networks,
            assets,
            shards,
            crossShardConnections,
            capabilities
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bootstrapArtifacts = definition.Assets.Where(item => item.Bootstrap is not null)
            .Select(item => (item.Bootstrap!.ProfileId, item.Bootstrap.Version))
            .Distinct()
            .Count();
        var infrastructureFragments = shards.Sum(shard => shard.InfrastructureKeys?.Count ?? 0);
        var observationPointEstimate = networks.Length + infrastructureFragments + shards.Length +
                                       definition.Assets.Count(item =>
                                           item.EndpointObservation != TeamLabEndpointObservationMode.Disabled);
        return new TeamLabPlanModel(
            topologyId,
            releaseId,
            networks,
            assets,
            shards,
            crossShardConnections,
            capabilities,
            [],
            $"sha256:{Convert.ToHexStringLower(SHA256.HashData(hashPayload))}",
            definition.Infrastructure.Count,
            bootstrapArtifacts,
            observationPointEstimate);
    }

    internal static IReadOnlyList<TeamLabInternalPlacement>? BuildPlacement(
        TeamLabExecutionTopology definition,
        IReadOnlyList<TeamLabPlanningNodeSnapshot> nodes)
    {
        var rawGroups = BuildGroups(definition);
        var edges = BuildPlacementEdges(definition, rawGroups);
        var groups = rawGroups.OrderByDescending(group => group.IsEntry)
            .ThenByDescending(group => edges.Where(edge => edge.Touches(group.Key)).Sum(edge => edge.Weight))
            .ThenByDescending(group => group.VmSlots)
            .ThenByDescending(group => group.DockerSlots)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        return Place(groups, edges, nodes);
    }

    private static IReadOnlyList<TeamLabInternalPlacement>? Place(
        IReadOnlyList<TeamLabInternalNetworkGroup> groups,
        IReadOnlyList<TeamLabInternalPlacementEdge> edges,
        IReadOnlyList<TeamLabPlanningNodeSnapshot> nodes)
    {
        var candidates = nodes.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Id).ToArray();
        var total = groups.Aggregate(WorkloadResourceVector.Zero, (sum, item) => sum + item.Resources);
        var single = candidates.Where(node => CanPlace(node, total))
            .OrderByDescending(node => Score(node, total.DockerSlots, total.VmSlots))
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id)
            .FirstOrDefault();
        if (single is not null)
            return [new TeamLabInternalPlacement(single, groups.ToList())];

        var placements = new List<TeamLabInternalPlacement>();
        foreach (var group in groups)
        {
            var assignment = placements.SelectMany(item => item.Groups.Select(placed => (placed.Key, item.Node.Id)))
                .ToDictionary(item => item.Key, item => item.Id, StringComparer.Ordinal);
            var selected = candidates.Select(node =>
                {
                    var placement = placements.FirstOrDefault(item => item.Node.Id == node.Id);
                    var used = placement?.Groups.Aggregate(WorkloadResourceVector.Zero,
                        (sum, item) => sum + item.Resources) ?? WorkloadResourceVector.Zero;
                    var requested = used + group.Resources;
                    return new
                    {
                        Node = node,
                        Placement = placement,
                        Requested = requested,
                        CrossNodeEdges = edges.Where(edge => edge.Touches(group.Key))
                            .Sum(edge => assignment.TryGetValue(edge.Other(group.Key), out var otherNodeId) &&
                                         otherNodeId != node.Id
                                ? edge.Weight
                                : 0)
                    };
                })
                .Where(item => CanPlace(item.Node, item.Requested))
                .OrderBy(item => item.CrossNodeEdges)
                .ThenByDescending(item => item.Placement is not null)
                .ThenByDescending(item => Score(item.Node,
                    item.Requested.DockerSlots, item.Requested.VmSlots))
                .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Node.Id)
                .FirstOrDefault();
            if (selected is null)
                return null;
            var targetPlacement = selected.Placement;
            if (targetPlacement is null)
            {
                targetPlacement = new TeamLabInternalPlacement(selected.Node, []);
                placements.Add(targetPlacement);
            }
            targetPlacement.Groups.Add(group);
        }
        return placements;
    }

    private static bool CanPlace(TeamLabPlanningNodeSnapshot node, WorkloadResourceVector requested) =>
        (requested.DockerSlots == 0 || node.SupportsDocker) &&
        (requested.VmSlots == 0 || node.SupportsVm) &&
        requested.DockerSlots <= node.AvailableDockerSlots &&
        requested.VmSlots <= node.AvailableVmSlots &&
        (node.AvailableResources == WorkloadResourceVector.Zero || node.AvailableResources.CanFit(requested));

    private static float Score(TeamLabPlanningNodeSnapshot node, int dockerSlots, int vmSlots) =>
        1000 * (1 - Math.Clamp(node.CpuLoad, 0, 1)) +
        500 * (1 - Math.Clamp(node.MemoryLoad, 0, 1)) +
        250 * (1 - (float)dockerSlots / Math.Max(node.AvailableDockerSlots, 1)) +
        250 * (1 - (float)vmSlots / Math.Max(node.AvailableVmSlots, 1));

    internal static IReadOnlyList<TeamLabInternalNetworkGroup> BuildGroups(TeamLabExecutionTopology definition)
    {
        var parent = definition.Networks.ToDictionary(item => item.Key, item => item.Key, StringComparer.Ordinal);
        foreach (var asset in definition.Assets.Where(item => item.IsImageBacked && item.Interfaces.Count > 1))
        {
            var keys = asset.Interfaces.Select(item => item.NetworkKey).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var key in keys.Skip(1)) Union(parent, keys[0], key);
        }
        return definition.Networks.GroupBy(item => Find(parent, item.Key), StringComparer.Ordinal)
            .Select(group =>
            {
                var networkKeys = group.Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal).ToArray();
                var networkSet = networkKeys.ToHashSet(StringComparer.Ordinal);
                var groupAssets = definition.Assets.Where(asset => asset.Interfaces.Any(iface => networkSet.Contains(iface.NetworkKey)))
                    .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
                var resources = groupAssets.Aggregate(WorkloadResourceVector.Zero, (sum, asset) =>
                    sum + new WorkloadResourceVector(
                        asset.CpuUnits,
                        asset.MemoryMiB,
                        asset.StorageMiB,
                        asset.Kind == TeamLabAssetKind.Docker ? 1 : 0,
                        asset.Kind == TeamLabAssetKind.Vm ? 1 : 0));
                return new TeamLabInternalNetworkGroup(
                    string.Join(',', networkKeys),
                    networkKeys,
                    groupAssets.Select(item => item.Key).ToArray(),
                    resources,
                    group.Any(item => item.IsEntry));
            }).ToArray();
    }

    private static IReadOnlyList<TeamLabInternalPlacementEdge> BuildPlacementEdges(
        TeamLabExecutionTopology definition,
        IReadOnlyList<TeamLabInternalNetworkGroup> groups)
    {
        var groupByNetwork = groups.SelectMany(group => group.NetworkKeys.Select(key => (key, group.Key)))
            .ToDictionary(item => item.key, item => item.Key, StringComparer.Ordinal);
        return definition.Connections
            .Where(item => groupByNetwork[item.FromNetworkKey] != groupByNetwork[item.ToNetworkKey])
            .Select(item => new TeamLabInternalPlacementEdge(
                groupByNetwork[item.FromNetworkKey],
                groupByNetwork[item.ToNetworkKey],
                item.Direction == TeamLabConnectionDirection.Bidirectional ? 2 : 1))
            .OrderBy(item => item.Left, StringComparer.Ordinal)
            .ThenBy(item => item.Right, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Find(IDictionary<string, string> parent, string key)
    {
        var value = parent[key];
        if (value == key) return key;
        return parent[key] = Find(parent, value);
    }

    private static void Union(IDictionary<string, string> parent, string left, string right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (leftRoot == rightRoot) return;
        if (string.CompareOrdinal(leftRoot, rightRoot) > 0) (leftRoot, rightRoot) = (rightRoot, leftRoot);
        parent[rightRoot] = leftRoot;
    }

    private static string FirstSubnet(string poolCidr, int prefixLength)
    {
        var parts = poolCidr.Split('/');
        var address = IPAddress.Parse(parts[0]);
        if (address.AddressFamily != AddressFamily.InterNetwork) return string.Empty;
        return $"{address}/{prefixLength}";
    }

    internal sealed record TeamLabInternalNetworkGroup(
        string Key,
        IReadOnlyList<string> NetworkKeys,
        IReadOnlyList<string> AssetKeys,
        WorkloadResourceVector Resources,
        bool IsEntry)
    {
        public int DockerSlots => Resources.DockerSlots;
        public int VmSlots => Resources.VmSlots;
    }

    internal sealed record TeamLabInternalPlacement(
        TeamLabPlanningNodeSnapshot Node,
        List<TeamLabInternalNetworkGroup> Groups);

    private sealed record TeamLabInternalPlacementEdge(string Left, string Right, int Weight)
    {
        public bool Touches(string key) => Left == key || Right == key;
        public string Other(string key) => Left == key ? Right : Left;
    }
}
