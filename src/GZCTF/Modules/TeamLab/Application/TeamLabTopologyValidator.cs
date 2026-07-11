using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public sealed partial class TeamLabTopologyValidator
{
    public const int MaxNetworks = 32;
    public const int MaxAssets = 128;
    public const int MaxInterfacesPerAsset = 8;

    public TeamLabValidationResultModel Validate(TeamLabTopologyDefinitionModel definition)
    {
        var issues = new List<TeamLabValidationIssueModel>();
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Trim().Length > 128)
            Add(issues, "topology_name_invalid", "name", "Topology name must contain between 1 and 128 characters.");

        if (definition.Networks.Count is < 1 or > MaxNetworks)
            Add(issues, "network_count_invalid", "networks", $"A topology must contain between 1 and {MaxNetworks} networks.");
        if (definition.Assets.Count is < 1 or > MaxAssets)
            Add(issues, "asset_count_invalid", "assets", $"A topology must contain between 1 and {MaxAssets} assets.");

        ValidateUniqueKeys(definition.Networks.Select(item => item.Key), "networks", issues);
        ValidateUniqueKeys(definition.Assets.Select(item => item.Key), "assets", issues);
        ValidateUniqueKeys(definition.Connections.Select(item => item.Key), "connections", issues);

        if (definition.Networks.Count(item => item.IsEntry) != 1)
            Add(issues, "entry_network_invalid", "networks", "Exactly one player entry network is required.");

        var ranges = new List<(string Key, uint Start, uint End)>();
        foreach (var (network, index) in definition.Networks.Select((value, index) => (value, index)))
        {
            ValidateKey(network.Key, $"networks[{index}].key", issues);
            if (string.IsNullOrWhiteSpace(network.Name) || network.Name.Trim().Length > 128)
                Add(issues, "network_name_invalid", $"networks[{index}].name", "Network name is required and cannot exceed 128 characters.");
            if (!TryRange(network.AddressPool.PoolCidr, out var start, out var end, out var poolPrefix))
            {
                Add(issues, "address_pool_invalid", $"networks[{index}].addressPool.poolCidr", "Address pool must be a valid IPv4 CIDR.");
                continue;
            }
            if (!IsRfc1918(start, end))
                Add(issues, "address_pool_not_private", $"networks[{index}].addressPool.poolCidr", "Address pool must be inside RFC1918 space.");
            if (network.AddressPool.RuntimePrefixLength <= poolPrefix || network.AddressPool.RuntimePrefixLength > 29)
                Add(issues, "runtime_prefix_invalid", $"networks[{index}].addressPool.runtimePrefixLength", "Runtime prefix must be more specific than the pool and no smaller than /29.");
            foreach (var existing in ranges.Where(existing => start <= existing.End && existing.Start <= end))
                Add(issues, "address_pool_overlap", $"networks[{index}].addressPool.poolCidr", $"Address pool overlaps network '{existing.Key}'.");
            ranges.Add((network.Key, start, end));
        }

        var networkByKey = definition.Networks
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var assetByKey = definition.Assets
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var (asset, assetIndex) in definition.Assets.Select((value, index) => (value, index)))
        {
            ValidateKey(asset.Key, $"assets[{assetIndex}].key", issues);
            if (string.IsNullOrWhiteSpace(asset.Name) || asset.Name.Trim().Length > 128)
                Add(issues, "asset_name_invalid", $"assets[{assetIndex}].name", "Asset name is required and cannot exceed 128 characters.");
            if (asset.ImageTemplateId <= 0)
                Add(issues, "image_template_invalid", $"assets[{assetIndex}].imageTemplateId", "A positive image template ID is required.");
            if (asset.Resources.CpuUnits <= 0 || asset.Resources.MemoryMiB <= 0 || asset.Resources.StorageMiB <= 0)
                Add(issues, "asset_resources_invalid", $"assets[{assetIndex}].resources", "CPU, memory and storage must be positive.");
            if (asset.Interfaces.Count is < 1 or > MaxInterfacesPerAsset)
                Add(issues, "interface_count_invalid", $"assets[{assetIndex}].interfaces", $"An asset must contain between 1 and {MaxInterfacesPerAsset} interfaces.");
            if (asset.Interfaces.Count(item => item.Primary) != 1)
                Add(issues, "primary_interface_invalid", $"assets[{assetIndex}].interfaces", "Exactly one primary interface is required.");
            ValidateUniqueKeys(asset.Interfaces.Select(item => item.Key), $"assets[{assetIndex}].interfaces", issues);

            foreach (var (iface, ifaceIndex) in asset.Interfaces.Select((value, index) => (value, index)))
            {
                var path = $"assets[{assetIndex}].interfaces[{ifaceIndex}]";
                ValidateKey(iface.Key, $"{path}.key", issues);
                if (!networkByKey.TryGetValue(iface.NetworkKey, out var network))
                {
                    Add(issues, "interface_network_missing", $"{path}.networkKey", $"Network '{iface.NetworkKey}' does not exist.");
                    continue;
                }
                var hostCapacity = 1L << (32 - network.AddressPool.RuntimePrefixLength);
                if (iface.HostOffset < 3 || iface.HostOffset >= hostCapacity - 1)
                    Add(issues, "interface_host_offset_reserved", $"{path}.hostOffset", "Host offset must avoid network, gateway, DHCP/DNS and broadcast reservations.");
            }

            if (asset.Environment is not null)
            {
                foreach (var key in asset.Environment.Keys.Where(key => !EnvironmentKeyRegex().IsMatch(key)))
                    Add(issues, "environment_key_invalid", $"assets[{assetIndex}].environment.{key}", "Environment keys must match [A-Z_][A-Z0-9_]{0,63}.");
            }
        }

        foreach (var (connection, index) in definition.Connections.Select((value, index) => (value, index)))
        {
            ValidateKey(connection.Key, $"connections[{index}].key", issues);
            if (!networkByKey.ContainsKey(connection.FromNetworkKey) || !networkByKey.ContainsKey(connection.ToNetworkKey))
                Add(issues, "connection_network_missing", $"connections[{index}]", "Both connection networks must exist.");
            if (string.Equals(connection.FromNetworkKey, connection.ToNetworkKey, StringComparison.Ordinal))
                Add(issues, "connection_self_reference", $"connections[{index}]", "A connection must join two different networks.");
            if (!assetByKey.TryGetValue(connection.ViaAssetKey, out var router) || !router.RoutingEnabled)
            {
                Add(issues, "connection_router_invalid", $"connections[{index}].viaAssetKey", "The connection router must exist and enable routing.");
                continue;
            }
            var routerNetworks = router.Interfaces.Select(item => item.NetworkKey).ToHashSet(StringComparer.Ordinal);
            if (!routerNetworks.Contains(connection.FromNetworkKey) || !routerNetworks.Contains(connection.ToNetworkKey))
                Add(issues, "connection_router_not_attached", $"connections[{index}].viaAssetKey", "The router must have interfaces on both connection networks.");
        }

        return new TeamLabValidationResultModel(issues.Count == 0, issues);
    }

    private static void ValidateUniqueKeys(IEnumerable<string> keys, string path, ICollection<TeamLabValidationIssueModel> issues)
    {
        foreach (var duplicate in keys.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1))
            Add(issues, "topology_key_duplicate", path, $"Key '{duplicate.Key}' occurs more than once.");
    }

    private static void ValidateKey(string key, string path, ICollection<TeamLabValidationIssueModel> issues)
    {
        if (!TopologyKeyRegex().IsMatch(key ?? string.Empty))
            Add(issues, "topology_key_invalid", path, "Keys must match [a-z][a-z0-9-]{0,62}.");
    }

    private static bool TryRange(string cidr, out uint start, out uint end, out int prefix)
    {
        start = end = 0;
        prefix = 0;
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out prefix) ||
            prefix is < 1 or > 30)
            return false;
        var raw = ToUInt32(address);
        var mask = uint.MaxValue << (32 - prefix);
        start = raw & mask;
        end = start + (1u << (32 - prefix)) - 1;
        return raw == start;
    }

    private static bool IsRfc1918(uint start, uint end) =>
        IsInside(start, end, "10.0.0.0/8") ||
        IsInside(start, end, "172.16.0.0/12") ||
        IsInside(start, end, "192.168.0.0/16");

    private static bool IsInside(uint start, uint end, string cidr) =>
        TryRange(cidr, out var allowedStart, out var allowedEnd, out _) && start >= allowedStart && end <= allowedEnd;

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static void Add(ICollection<TeamLabValidationIssueModel> issues, string code, string path, string message) =>
        issues.Add(new TeamLabValidationIssueModel(code, path, message));

    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex TopologyKeyRegex();

    [GeneratedRegex("^[A-Z_][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentKeyRegex();
}
