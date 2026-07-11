using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabPlanningNodeSnapshot(
    Guid Id,
    string Name,
    bool SupportsDocker,
    bool SupportsVm,
    int AvailableDockerSlots,
    int AvailableVmSlots,
    float CpuLoad,
    float MemoryLoad);

public static class TeamLabAssetPlanner
{
    public static TeamLabPlanModel Build(
        Guid topologyId,
        Guid releaseId,
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyList<TeamLabPlanningNodeSnapshot> nodes)
    {
        var networks = definition.Networks
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new TeamLabPlanNetworkModel(
                item.Key,
                item.Name,
                FirstSubnet(item.AddressPool.PoolCidr, item.AddressPool.RuntimePrefixLength),
                item.IsEntry))
            .ToArray();
        var assets = definition.Assets
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new TeamLabPlanAssetModel(
                item.Key,
                item.Name,
                item.Kind,
                item.ImageTemplateId,
                item.Resources,
                item.Interfaces.Select(iface => new TeamLabPlanInterfaceModel(
                    iface.Key, iface.NetworkKey, iface.HostOffset, iface.Primary)).ToArray(),
                item.RoutingEnabled))
            .ToArray();

        var groups = BuildGroups(definition).OrderByDescending(group => group.IsEntry)
            .ThenByDescending(group => group.VmSlots)
            .ThenByDescending(group => group.DockerSlots)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var placements = Place(groups, nodes);
        if (placements is null)
            throw new TeamLabApiContractException(
                "capability_unavailable",
                "The current TeamLab node set cannot place this topology.",
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
        return new TeamLabPlanModel(
            topologyId,
            releaseId,
            networks,
            assets,
            shards,
            crossShardConnections,
            capabilities,
            [],
            $"sha256:{Convert.ToHexStringLower(SHA256.HashData(hashPayload))}");
    }

    private static IReadOnlyList<Placement>? Place(
        IReadOnlyList<NetworkGroup> groups,
        IReadOnlyList<TeamLabPlanningNodeSnapshot> nodes)
    {
        var candidates = nodes.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Id).ToArray();
        var totalDocker = groups.Sum(item => item.DockerSlots);
        var totalVm = groups.Sum(item => item.VmSlots);
        var single = candidates.Where(node => CanPlace(node, totalDocker, totalVm))
            .OrderByDescending(node => Score(node, totalDocker, totalVm))
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id)
            .FirstOrDefault();
        if (single is not null)
            return [new Placement(single, groups.ToList())];

        var placements = new List<Placement>();
        foreach (var group in groups)
        {
            var selected = candidates.Select(node =>
                {
                    var placement = placements.FirstOrDefault(item => item.Node.Id == node.Id);
                    var usedDocker = placement?.Groups.Sum(item => item.DockerSlots) ?? 0;
                    var usedVm = placement?.Groups.Sum(item => item.VmSlots) ?? 0;
                    return new { Node = node, Placement = placement, UsedDocker = usedDocker, UsedVm = usedVm };
                })
                .Where(item => CanPlace(item.Node, item.UsedDocker + group.DockerSlots, item.UsedVm + group.VmSlots))
                .OrderByDescending(item => item.Placement is not null)
                .ThenByDescending(item => Score(item.Node, item.UsedDocker + group.DockerSlots, item.UsedVm + group.VmSlots))
                .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Node.Id)
                .FirstOrDefault();
            if (selected is null)
                return null;
            var targetPlacement = selected.Placement;
            if (targetPlacement is null)
            {
                targetPlacement = new Placement(selected.Node, []);
                placements.Add(targetPlacement);
            }
            targetPlacement.Groups.Add(group);
        }
        return placements;
    }

    private static bool CanPlace(TeamLabPlanningNodeSnapshot node, int dockerSlots, int vmSlots) =>
        (dockerSlots == 0 || node.SupportsDocker) &&
        (vmSlots == 0 || node.SupportsVm) &&
        dockerSlots <= node.AvailableDockerSlots &&
        vmSlots <= node.AvailableVmSlots;

    private static float Score(TeamLabPlanningNodeSnapshot node, int dockerSlots, int vmSlots) =>
        1000 * (1 - Math.Clamp(node.CpuLoad, 0, 1)) +
        500 * (1 - Math.Clamp(node.MemoryLoad, 0, 1)) +
        250 * (1 - (float)dockerSlots / Math.Max(node.AvailableDockerSlots, 1)) +
        250 * (1 - (float)vmSlots / Math.Max(node.AvailableVmSlots, 1));

    private static IReadOnlyList<NetworkGroup> BuildGroups(TeamLabTopologyDefinitionModel definition)
    {
        var parent = definition.Networks.ToDictionary(item => item.Key, item => item.Key, StringComparer.Ordinal);
        foreach (var asset in definition.Assets)
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
                return new NetworkGroup(
                    string.Join(',', networkKeys),
                    networkKeys,
                    groupAssets.Select(item => item.Key).ToArray(),
                    groupAssets.Count(item => item.Kind == TeamLabAssetKind.Docker),
                    groupAssets.Count(item => item.Kind == TeamLabAssetKind.Vm),
                    group.Any(item => item.IsEntry));
            }).ToArray();
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

    private sealed record NetworkGroup(
        string Key,
        IReadOnlyList<string> NetworkKeys,
        IReadOnlyList<string> AssetKeys,
        int DockerSlots,
        int VmSlots,
        bool IsEntry);

    private sealed record Placement(TeamLabPlanningNodeSnapshot Node, List<NetworkGroup> Groups);
}
