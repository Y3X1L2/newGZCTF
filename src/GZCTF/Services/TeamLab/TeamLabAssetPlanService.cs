using System.Net;
using System.Text.RegularExpressions;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.Fleet;
using GZCTF.Utils;

namespace GZCTF.Services.TeamLab;

public enum TeamLabAssetSpecKind
{
    Docker,
    Vm
}

public sealed record TeamLabAssetInterfaceSpec(
    string NodeKey,
    string NetworkKey,
    string BridgeName,
    string InterfaceName,
    string IpAddress,
    int PrefixLength,
    string MacAddress,
    bool IsPrimary,
    bool RemoveDefaultRoute);

public sealed record TeamLabAssetSpec(
    TeamLabAssetSpecKind Kind,
    string TopologyKey,
    string Name,
    int? SourceTemplateId,
    string Image,
    int CpuCount,
    int MemoryLimit,
    int StorageLimit,
    int ExposePort,
    string? InfrastructureRole,
    int StartPriority,
    IReadOnlyList<TeamLabAssetInterfaceSpec> Interfaces,
    OSType OSType = OSType.Linux);

public sealed record TeamLabRuntimeNetworkSpec(
    string TopologyKey,
    string Name,
    string Cidr,
    string GatewayIp,
    string BridgeName);

public sealed record TeamLabPublishedAssetPlanResult(
    bool Success,
    string Message,
    IReadOnlyList<TeamLabRuntimeNetworkSpec> Networks,
    IReadOnlyList<TeamLabAssetSpec> Assets)
{
    public static TeamLabPublishedAssetPlanResult Failed(string message) => new(false, message, [], []);
}

public static partial class TeamLabAssetPlanService
{
    public static TeamLabPublishedAssetPlanResult BuildPublishedAssetPlan(PenetrationConfig config, int runtimeId,
        int teamIndex, IReadOnlyDictionary<int, ImageTemplate> templates, string? runtimeTeamCidr = null)
    {
        if (config.Networks.Count == 0)
            return TeamLabPublishedAssetPlanResult.Failed("TeamLab published topology has no LabNetwork.");

        var hasRuntimeTeamCidr = !string.IsNullOrWhiteSpace(runtimeTeamCidr);
        var teamCidr = !hasRuntimeTeamCidr
            ? AllocateSubnet(config.BaseCidr, config.TeamSubnetPrefix, teamIndex)
            : runtimeTeamCidr;
        if (string.IsNullOrWhiteSpace(teamCidr))
            return TeamLabPublishedAssetPlanResult.Failed("TeamLab published topology has an invalid base CIDR.");

        var networkSpecs = new List<TeamLabRuntimeNetworkSpec>();
        var networkById = new Dictionary<int, TeamLabRuntimeNetworkSpec>();
        foreach (var network in config.Networks.OrderBy(n => n.OrderIndex))
        {
            var hasExplicitCidr = !string.IsNullOrWhiteSpace(network.Cidr);
            var cidr = !hasExplicitCidr
                ? AllocateSubnet(teamCidr, config.NetworkSubnetPrefix, network.OrderIndex)
                : network.Cidr!.Trim();
            if (string.IsNullOrWhiteSpace(cidr) || !IsValidIpv4Cidr(cidr))
                return TeamLabPublishedAssetPlanResult.Failed($"LabNetwork {network.Name} has an invalid CIDR.");

            var spec = new TeamLabRuntimeNetworkSpec(
                network.TopologyKey,
                network.Name,
                cidr,
                FirstHost(cidr),
                TrimLinuxName($"{TeamLabPlanService.BuildRuntimeResourcePrefix(runtimeId)}-{network.TopologyKey}"));
            networkSpecs.Add(spec);
            networkById[network.Id] = spec;
        }

        var networkValidation = ValidateRuntimeNetworks(networkSpecs);
        if (!string.IsNullOrWhiteSpace(networkValidation))
            return TeamLabPublishedAssetPlanResult.Failed(networkValidation);

        var assets = new List<TeamLabAssetSpec>();
        var addressCounters = networkSpecs.ToDictionary(n => n.TopologyKey, _ => 3);
        foreach (var node in config.Nodes.OrderBy(n => n.OrderIndex))
        {
            if (node.ImageTemplateId is not { } templateId ||
                !templates.TryGetValue(templateId, out var template) ||
                template.Status != ImageStatus.Ready)
                return TeamLabPublishedAssetPlanResult.Failed(
                    $"Node {node.Name} must bind a ready image template before TeamLab deployment.");

            var interfaces = ResolveNodeInterfaces(config, node)
                .Select(iface =>
                {
                    var network = networkById[iface.Network.Id];
                    var ip = !string.IsNullOrWhiteSpace(iface.StaticIp)
                        ? iface.StaticIp!
                        : NextHost(network.Cidr, addressCounters[network.TopologyKey]++);
                    return new TeamLabAssetInterfaceSpec(
                        node.TopologyKey,
                        iface.Network.TopologyKey,
                        network.BridgeName,
                        iface.Name,
                        ip,
                        PrefixLength(network.Cidr),
                        BuildMacAddress(runtimeId, node.TopologyKey, iface.Name),
                        iface.IsPrimary,
                        RemoveDefaultRoute: false);
                })
                .ToArray();

            if (interfaces.Length == 0)
                return TeamLabPublishedAssetPlanResult.Failed($"Node {node.Name} has no TeamLab interface.");

            assets.Add(template.ImageType == ImageType.Docker
                ? BuildDockerSpec(node, ResolveTemplateImage(template), interfaces)
                : BuildVmSpec(node, template, interfaces));
        }

        return new TeamLabPublishedAssetPlanResult(true, "TeamLab published asset plan built.", networkSpecs,
            assets.OrderBy(asset => asset.StartPriority).ThenBy(asset => asset.TopologyKey, StringComparer.Ordinal).ToArray());
    }

    public static TeamLabAssetSpec BuildDockerSpec(PenetrationNode node, string image,
        IReadOnlyList<TeamLabAssetInterfaceSpec> interfaces) =>
        new(TeamLabAssetSpecKind.Docker, node.TopologyKey, node.Name, node.ImageTemplateId, image,
            node.CpuCount, node.MemoryLimit, node.StorageLimit, node.ExposePort, ResolveInfrastructureRole(node),
            ResolveStartPriority(node), interfaces);

    public static TeamLabAssetSpec BuildVmSpec(PenetrationNode node, ImageTemplate template,
        IReadOnlyList<TeamLabAssetInterfaceSpec> interfaces)
    {
        if (template.ImageType == ImageType.Docker)
            throw new ArgumentException("VM TeamLab asset requires a VM image template.", nameof(template));

        return new TeamLabAssetSpec(TeamLabAssetSpecKind.Vm, node.TopologyKey, node.Name, template.Id,
            template.LocalFilePath ?? template.Name, node.CpuCount, node.MemoryLimit, node.StorageLimit,
            node.ExposePort, ResolveInfrastructureRole(node), ResolveStartPriority(node), interfaces,
            template.OSType);
    }

    public static TeamLabContainerAttachRequest BuildContainerAttachRequest(int runtimeId, string containerId,
        TeamLabAssetInterfaceSpec iface, bool dryRun, string? gatewayIp = null, string[]? staticRoutes = null,
        string[]? dnsServers = null) =>
        new(runtimeId, containerId, iface.BridgeName, BuildHostInterfaceName(runtimeId, iface.NodeKey,
                iface.NetworkKey, iface.InterfaceName), iface.InterfaceName, $"{iface.IpAddress}/{iface.PrefixLength}",
            iface.MacAddress, iface.RemoveDefaultRoute, gatewayIp, staticRoutes ?? [], dnsServers ?? [], dryRun);

    public static AgentVmNetworkInterfaceRequest ToVmInterfaceRequest(TeamLabAssetInterfaceSpec iface,
        OSType osType = OSType.Linux) =>
        new()
        {
            BridgeName = iface.BridgeName,
            MacAddress = iface.MacAddress,
            Model = osType == OSType.Windows ? "e1000e" : "virtio",
            InterfaceName = iface.InterfaceName,
            IpAddress = iface.IpAddress,
            PrefixLength = iface.PrefixLength,
            Gateway = iface.IsPrimary ? GatewayFromHost(iface.IpAddress, iface.PrefixLength) : null,
            DnsServers = iface.IsPrimary ? [GatewayFromHost(iface.IpAddress, iface.PrefixLength)] : [],
            IsPrimary = iface.IsPrimary
        };

    public static string BuildMacAddress(int runtimeId, string topologyKey, string interfaceName)
    {
        var hash = StableHash($"{runtimeId}:{topologyKey}:{interfaceName}");
        return $"02:42:{(hash >> 24) & 0xff:x2}:{(hash >> 16) & 0xff:x2}:{(hash >> 8) & 0xff:x2}:{hash & 0xff:x2}";
    }

    public static string BuildHostInterfaceName(int runtimeId, string nodeKey, string networkKey, string interfaceName)
    {
        var hash = StableHash($"{runtimeId}:{nodeKey}:{networkKey}:{interfaceName}") & 0xfffff;
        return $"tl{runtimeId}v{hash:x5}";
    }

    public static bool IsValidIpv4Cidr(string cidr)
    {
        var parts = cidr.Split('/');
        return parts.Length == 2 &&
               IPAddress.TryParse(parts[0], out var ip) &&
               ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
               int.TryParse(parts[1], out var prefix) &&
               prefix is >= 1 and <= 32;
    }

    private static string? ValidateRuntimeNetworks(IReadOnlyList<TeamLabRuntimeNetworkSpec> networks)
    {
        var ranges = new List<(TeamLabRuntimeNetworkSpec Network, uint Start, uint End)>();
        foreach (var network in networks)
        {
            var range = TryParseIpv4CidrRange(network.Cidr);
            if (range is null)
                return $"LabNetwork {network.Name} has an invalid CIDR.";

            if (range.Value.Prefix > 29)
                return $"LabNetwork {network.Name} CIDR must be /29 or larger to provide gateway and asset host addresses.";

            if (!IsRfc1918(range.Value.Start, range.Value.End))
                return $"LabNetwork {network.Name} CIDR must be inside RFC1918 private address space.";

            foreach (var existing in ranges)
            {
                if (range.Value.Start <= existing.End && existing.Start <= range.Value.End)
                    return $"LabNetwork {network.Name} CIDR overlaps with LabNetwork {existing.Network.Name}.";
            }

            ranges.Add((network, range.Value.Start, range.Value.End));
        }

        return null;
    }

    private static string NormalizeLinuxToken(string value)
    {
        var normalized = LinuxUnsafeRegex().Replace(value.ToLowerInvariant(), string.Empty);
        return string.IsNullOrWhiteSpace(normalized) ? "x" : normalized;
    }

    private static IReadOnlyList<PenetrationInterface> ResolveNodeInterfaces(PenetrationConfig config,
        PenetrationNode node)
    {
        if (node.Interfaces.Count > 0)
        {
            if (node.Interfaces.All(i => !i.IsPrimary))
                node.Interfaces.OrderBy(i => i.OrderIndex).First().IsPrimary = true;

            return node.Interfaces.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.OrderIndex).ToArray();
        }

        var network = node.Network ?? config.Networks.First(n => n.Id == node.NetworkId);
        return
        [
            new PenetrationInterface
            {
                Id = -(node.Id * 1000 + 1),
                TopologyKey = $"{node.TopologyKey}:eth0",
                Node = node,
                NodeId = node.Id,
                Network = network,
                NetworkId = network.Id,
                Name = "eth0",
                StaticIp = node.StaticIp,
                IsPrimary = true,
                IsManagement = false,
                OrderIndex = 0
            }
        ];
    }

    private static string ResolveTemplateImage(ImageTemplate template) =>
        template.ImageType == ImageType.Docker
            ? DockerImageReference.ResolvePullTarget(template.Name, template.RegistryUrl).FullImage
            : !string.IsNullOrWhiteSpace(template.LocalFilePath) ? template.LocalFilePath : template.Name;

    private static string? ResolveInfrastructureRole(PenetrationNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.ReservedAdRole))
            return node.ReservedAdRole.Trim();

        return node.NodeType == PenetrationNodeType.DomainControllerReserved ? "DomainController" : null;
    }

    private static int ResolveStartPriority(PenetrationNode node)
    {
        var role = ResolveInfrastructureRole(node);
        return role?.ToLowerInvariant() switch
        {
            "domaincontroller" or "dns" => 10,
            "bastion" => 20,
            "domainmember" => 30,
            _ => 50
        };
    }

    private static string AllocateSubnet(string baseCidr, int prefixLength, int index)
    {
        var parts = baseCidr.Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var baseAddress) ||
            !int.TryParse(parts[1], out var basePrefix) ||
            baseAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            basePrefix is < 1 or > 30 ||
            prefixLength < basePrefix ||
            prefixLength > 30)
            return string.Empty;

        var subnetSize = 1u << (32 - prefixLength);
        var subnetCount = 1u << (prefixLength - basePrefix);
        var safeIndex = subnetCount == 0 ? 0u : (uint)Math.Max(0, index) % subnetCount;
        return $"{FromUInt32(ToUInt32(baseAddress) + safeIndex * subnetSize)}/{prefixLength}";
    }

    private static (uint Start, uint End, int Prefix)? TryParseIpv4CidrRange(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) ||
            prefix is < 1 or > 32)
            return null;

        var mask = prefix == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        var start = ToUInt32(address) & mask;
        var size = prefix == 32 ? 1u : 1u << (32 - prefix);
        return (start, start + size - 1, prefix);
    }

    private static bool IsRfc1918(uint start, uint end)
    {
        return IsInside(start, end, "10.0.0.0/8") ||
               IsInside(start, end, "172.16.0.0/12") ||
               IsInside(start, end, "192.168.0.0/16");
    }

    private static bool IsInside(uint start, uint end, string cidr)
    {
        var range = TryParseIpv4CidrRange(cidr);
        return range is not null && start >= range.Value.Start && end <= range.Value.End;
    }

    private static string FirstHost(string cidr) => NextHost(cidr, 1);

    private static string GatewayFromHost(string ipAddress, int prefix)
    {
        if (!IPAddress.TryParse(ipAddress, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            prefix is < 1 or > 30)
            return string.Empty;

        var mask = uint.MaxValue << (32 - prefix);
        var network = ToUInt32(address) & mask;
        return FromUInt32(network + 1).ToString();
    }

    private static string NextHost(string cidr, int offset)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var address) ||
            !int.TryParse(parts[1], out var prefix) ||
            prefix is < 1 or > 30)
            return string.Empty;

        var size = 1u << (32 - prefix);
        if (offset <= 0 || (uint)offset >= size - 1)
            return string.Empty;

        return FromUInt32(ToUInt32(address) + (uint)offset).ToString();
    }

    private static int PrefixLength(string cidr)
    {
        var parts = cidr.Split('/');
        return parts.Length == 2 && int.TryParse(parts[1], out var prefix) ? prefix : 32;
    }

    private static string TrimLinuxName(string value) => value.Length <= 15 ? value : value[..15];

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3]
            : 0;
    }

    private static IPAddress FromUInt32(uint value) => new([
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ]);

    private static uint StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash;
        }
    }

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex LinuxUnsafeRegex();
}
