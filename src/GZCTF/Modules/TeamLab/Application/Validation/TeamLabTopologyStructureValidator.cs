using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application.Validation;

internal sealed partial class TeamLabTopologyStructureValidator(TeamLabAddressPolicy addressPolicy)
{
    public void Validate(
        TeamLabTopologyDefinitionModel definition,
        int schemaVersion,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Trim().Length > 128)
            Add(issues, "topology_name_invalid", "name", "Topology name must contain between 1 and 128 characters.");
        if (definition.Networks.Count is < 1 or > TeamLabTopologyValidator.MaxNetworks)
            Add(issues, "network_count_invalid", "networks",
                $"A topology must contain between 1 and {TeamLabTopologyValidator.MaxNetworks} networks.");
        if (definition.Assets.Count is < 1 or > TeamLabTopologyValidator.MaxAssets)
            Add(issues, "asset_count_invalid", "assets",
                $"A topology must contain between 1 and {TeamLabTopologyValidator.MaxAssets} assets.");

        var infrastructure = definition.Infrastructure ?? [];
        var dependencies = definition.Dependencies ?? [];
        if (schemaVersion == 1 &&
            (infrastructure.Count > 0 || dependencies.Count > 0 || definition.Observation is not null ||
             definition.Assets.Any(asset => asset.EndpointObservation != TeamLabEndpointObservationMode.Disabled) ||
             definition.Connections.Any(connection => connection.ViaNodeKey is not null || connection.Direction is not null)))
        {
            Add(issues, "topology_schema_mismatch", "schemaVersion",
                "Schema version 1 cannot contain schema version 2 fields.");
        }

        ValidateUniqueKeys(definition.Networks.Select(item => item.Key), "networks", issues);
        ValidateUniqueKeys(definition.Assets.Select(item => item.Key), "assets", issues);
        ValidateUniqueKeys(infrastructure.Select(item => item.Key), "infrastructure", issues);
        ValidateUniqueKeys(definition.Connections.Select(item => item.Key), "connections", issues);
        ValidateUniqueKeys(
            definition.Assets.Select(item => item.Key).Concat(infrastructure.Select(item => item.Key)),
            "nodes",
            issues);

        if (definition.Networks.Count(item => item.IsEntry) != 1)
            Add(issues, "entry_network_invalid", "networks", "Exactly one player entry network is required.");

        var ranges = new List<(string Key, uint Start, uint End)>();
        foreach (var (network, index) in definition.Networks.Select((value, index) => (value, index)))
        {
            ValidateKey(network.Key, $"networks[{index}].key", issues);
            ValidateName(network.Name, $"networks[{index}].name", "Network", issues);
            if (!TryRange(network.AddressPool.PoolCidr, out var start, out var end, out var poolPrefix))
            {
                Add(issues, "address_pool_invalid", $"networks[{index}].addressPool.poolCidr",
                    "Address pool must be a valid IPv4 CIDR.");
                continue;
            }
            if (!IsRfc1918(start, end))
                Add(issues, "address_pool_not_private", $"networks[{index}].addressPool.poolCidr",
                    "Address pool must be inside RFC1918 space.");
            // Runtime CIDRs derived from this pool are installed in the WorkerNode host routing
            // table, so anything the node already routes would be shadowed: both rules below keep
            // tenant addressing inside the range the platform owns.
            if (!addressPolicy.IsWithinAllowedPools(start, end))
                Add(issues, "address_pool_out_of_platform_range", $"networks[{index}].addressPool.poolCidr",
                    $"地址池必须完全落在平台运行时网段 {addressPolicy.AllowedPoolDescription} 之内，请改用该范围内的网段。");
            if (addressPolicy.TryFindReservedConflict(start, end, out var conflict))
                Add(issues, "address_pool_reserved", $"networks[{index}].addressPool.poolCidr",
                    $"地址池与平台保留网段 {conflict.Cidr} 冲突（{conflict.Reason}），请改用其他网段。");
            if (network.AddressPool.RuntimePrefixLength <= poolPrefix || network.AddressPool.RuntimePrefixLength > 29)
                Add(issues, "runtime_prefix_invalid", $"networks[{index}].addressPool.runtimePrefixLength",
                    "Runtime prefix must be more specific than the pool and no smaller than /29.");
            foreach (var existing in ranges.Where(existing => start <= existing.End && existing.Start <= end))
                Add(issues, "address_pool_overlap", $"networks[{index}].addressPool.poolCidr",
                    $"Address pool overlaps network '{existing.Key}'.");
            ranges.Add((network.Key, start, end));
        }

        var networkByKey = definition.Networks
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var infrastructureByKey = infrastructure
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var (asset, index) in definition.Assets.Select((value, index) => (value, index)))
            ValidateAsset(asset, index, networkByKey, issues);
        foreach (var (item, index) in infrastructure.Select((value, index) => (value, index)))
            ValidateInfrastructure(item, index, networkByKey, issues);
        foreach (var (connection, index) in definition.Connections.Select((value, index) => (value, index)))
            ValidateConnection(connection, index, networkByKey, infrastructureByKey, issues);
    }

    private static void ValidateAsset(
        TeamLabTopologyAssetModel asset,
        int index,
        IReadOnlyDictionary<string, TeamLabTopologyNetworkModel> networkByKey,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        var path = $"assets[{index}]";
        ValidateKey(asset.Key, $"{path}.key", issues);
        ValidateName(asset.Name, $"{path}.name", "Asset", issues);
        if (asset.ImageTemplateId <= 0)
            Add(issues, "image_template_invalid", $"{path}.imageTemplateId", "请为该资产选择可用镜像模板。");
        if (asset.Resources.CpuUnits <= 0 || asset.Resources.MemoryMiB <= 0 || asset.Resources.StorageMiB <= 0)
            Add(issues, "asset_resources_invalid", $"{path}.resources", "CPU, memory and storage must be positive.");
        ValidateInterfaces(asset.Interfaces, path, networkByKey, false, issues);
    }

    private static void ValidateInfrastructure(
        TeamLabTopologyInfrastructureModel infrastructure,
        int index,
        IReadOnlyDictionary<string, TeamLabTopologyNetworkModel> networkByKey,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        var path = $"infrastructure[{index}]";
        ValidateKey(infrastructure.Key, $"{path}.key", issues);
        ValidateName(infrastructure.Name, $"{path}.name", "Infrastructure node", issues);
        if (infrastructure.Kind == TeamLabInfrastructureKind.ManagedSwitch)
        {
            if (string.IsNullOrWhiteSpace(infrastructure.NetworkKey) ||
                !networkByKey.ContainsKey(infrastructure.NetworkKey))
                Add(issues, "managed_switch_network_invalid", $"{path}.networkKey",
                    "A managed switch must own exactly one existing network.");
            if (infrastructure.Interfaces.Count != 0)
                Add(issues, "managed_switch_interfaces_invalid", $"{path}.interfaces",
                    "A managed switch uses its network key and cannot define routed interfaces.");
            return;
        }

        if (infrastructure.NetworkKey is not null)
            Add(issues, "managed_router_network_invalid", $"{path}.networkKey",
                "A managed router defines routed interfaces instead of a switch network key.");
        ValidateInterfaces(infrastructure.Interfaces, path, networkByKey, true, issues);
        if (infrastructure.Interfaces.Select(item => item.NetworkKey).Distinct(StringComparer.Ordinal).Count() < 2)
            Add(issues, "managed_router_interfaces_invalid", $"{path}.interfaces",
                "A managed router must attach to at least two unique networks.");
    }

    private static void ValidateConnection(
        TeamLabTopologyConnectionModel connection,
        int index,
        IReadOnlyDictionary<string, TeamLabTopologyNetworkModel> networkByKey,
        IReadOnlyDictionary<string, TeamLabTopologyInfrastructureModel> infrastructureByKey,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        var path = $"connections[{index}]";
        ValidateKey(connection.Key, $"{path}.key", issues);
        if (!networkByKey.ContainsKey(connection.FromNetworkKey) || !networkByKey.ContainsKey(connection.ToNetworkKey))
            Add(issues, "connection_network_missing", path, "Both connection networks must exist.");
        if (string.Equals(connection.FromNetworkKey, connection.ToNetworkKey, StringComparison.Ordinal))
            Add(issues, "connection_self_reference", path, "A connection must join two different networks.");
        var hasNode = !string.IsNullOrWhiteSpace(connection.ViaNodeKey);
        if (!hasNode || !string.IsNullOrWhiteSpace(connection.ViaAssetKey))
        {
            Add(issues, "connection_path_invalid", path,
                "A connection must reference exactly one managed router.");
            return;
        }

        var targetPath = $"{path}.viaNodeKey";
        if (!infrastructureByKey.TryGetValue(connection.ViaNodeKey!, out var router) ||
            router.Kind != TeamLabInfrastructureKind.ManagedRouter)
        {
            Add(issues, "connection_router_invalid", targetPath,
                "The connection node must be a managed router.");
            return;
        }
        var attachedNetworks = router.Interfaces.Select(item => item.NetworkKey).ToHashSet(StringComparer.Ordinal);

        if (!attachedNetworks.Contains(connection.FromNetworkKey) || !attachedNetworks.Contains(connection.ToNetworkKey))
            Add(issues, "connection_router_not_attached", targetPath,
                "The router must have interfaces on both connection networks.");
    }

    private static void ValidateInterfaces(
        IReadOnlyList<TeamLabTopologyInterfaceModel> interfaces,
        string ownerPath,
        IReadOnlyDictionary<string, TeamLabTopologyNetworkModel> networkByKey,
        bool managedRouter,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        if (interfaces.Count is < 1 or > TeamLabTopologyValidator.MaxInterfacesPerAsset)
            Add(issues, "interface_count_invalid", $"{ownerPath}.interfaces",
                $"A node must contain between 1 and {TeamLabTopologyValidator.MaxInterfacesPerAsset} interfaces.");
        if (interfaces.Count(item => item.Primary) != 1)
            Add(issues, "primary_interface_invalid", $"{ownerPath}.interfaces",
                "Exactly one primary interface is required.");
        ValidateUniqueKeys(interfaces.Select(item => item.Key), $"{ownerPath}.interfaces", issues);
        foreach (var duplicate in interfaces.GroupBy(item => item.NetworkKey, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            Add(issues, "interface_network_duplicate", $"{ownerPath}.interfaces",
                $"Network '{duplicate.Key}' is attached more than once.");

        foreach (var (iface, index) in interfaces.Select((value, index) => (value, index)))
        {
            var path = $"{ownerPath}.interfaces[{index}]";
            ValidateKey(iface.Key, $"{path}.key", issues);
            if (!networkByKey.TryGetValue(iface.NetworkKey, out var network))
            {
                Add(issues, "interface_network_missing", $"{path}.networkKey",
                    $"Network '{iface.NetworkKey}' does not exist.");
                continue;
            }
            var hostCapacity = 1L << (32 - network.AddressPool.RuntimePrefixLength);
            var minimumOffset = managedRouter ? 1 : 3;
            if (iface.HostOffset < minimumOffset || iface.HostOffset >= hostCapacity - 2)
                Add(issues, "interface_host_offset_reserved", $"{path}.hostOffset",
                    managedRouter
                        ? "Managed router host offset must avoid the network, WireGuard server and broadcast addresses."
                        : "Host offset must avoid network, gateway, DHCP/DNS, WireGuard server and broadcast reservations.");
        }
    }

    private static void ValidateUniqueKeys(
        IEnumerable<string> keys,
        string path,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        foreach (var duplicate in keys.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1))
            Add(issues, "topology_key_duplicate", path, $"Key '{duplicate.Key}' occurs more than once.");
    }

    private static void ValidateKey(string key, string path, ICollection<TeamLabValidationIssueModel> issues)
    {
        if (!TopologyKeyRegex().IsMatch(key ?? string.Empty))
            Add(issues, "topology_key_invalid", path, "Keys must match [a-z][a-z0-9-]{0,62}.");
    }

    private static void ValidateName(
        string name,
        string path,
        string subject,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
            Add(issues, "topology_name_invalid", path, $"{subject} name is required and cannot exceed 128 characters.");
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
        TryRange(cidr, out var allowedStart, out var allowedEnd, out _) &&
        start >= allowedStart && end <= allowedEnd;

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static void Add(
        ICollection<TeamLabValidationIssueModel> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new TeamLabValidationIssueModel(code, path, message));

    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex TopologyKeyRegex();

    [GeneratedRegex("^[a-z][a-zA-Z0-9_.-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterKeyRegex();
}
