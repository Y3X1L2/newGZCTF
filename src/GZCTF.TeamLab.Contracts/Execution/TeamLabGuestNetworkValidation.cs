using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace GZCTF.TeamLab.Contracts.Execution;

/// <summary>Bounded, typed guest network input shared by control and execution planes.</summary>
public static class TeamLabGuestNetworkValidation
{
    public const int MaxDnsServers = 3;
    public const int MaxStaticRoutes = 8;

    public static bool IsInterfaceName(string? value) => value is null ||
        Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_-]{0,14}$", RegexOptions.CultureInvariant);

    // Explicit loopback DNS is useful for a domain controller. Unspecified and multicast addresses are not resolvers.
    public static bool IsDnsServer(string? value) => TryHost(value, out var address) &&
        address.GetAddressBytes()[0] is > 0 and < 224 && !address.Equals(IPAddress.Broadcast);

    public static bool IsRoute(TeamLabGuestRouteV2? route) => route is not null &&
        TryDestination(route.DestinationCidr, out _, out _) && IsNextHop(route.NextHop) &&
        route.Metric is null or >= 1 and <= 9999;

    public static bool IsNextHop(string? value) => TryHost(value, out var address) &&
        address.GetAddressBytes()[0] is > 0 and < 224 && !IPAddress.IsLoopback(address) &&
        !address.Equals(IPAddress.Broadcast);

    public static bool IsNextHopOnInterface(string? nextHop, string? interfaceIp, int prefix)
    {
        if (!IsNextHop(nextHop) || !TryHost(nextHop, out var hop) || !TryHost(interfaceIp, out var host) ||
            prefix is < 1 or > 30) return false;
        var mask = uint.MaxValue << (32 - prefix);
        var network = ToUInt32(host) & mask;
        var hopValue = ToUInt32(hop);
        return (hopValue & mask) == network && hopValue != network && hopValue != (network | ~mask) &&
            !hop.Equals(host);
    }

    public static bool TryDestination(string? cidr, out IPAddress address, out int prefix)
    {
        address = IPAddress.None;
        prefix = 0;
        var parts = cidr?.Split('/');
        if (parts is not { Length: 2 } || !TryHost(parts[0], out address) ||
            !int.TryParse(parts[1], out prefix) || prefix is < 1 or > 32) return false;
        var mask = uint.MaxValue << (32 - prefix);
        return (ToUInt32(address) & mask) == ToUInt32(address);
    }

    public static bool IsValid(TeamLabAssetExecutionSpecV2 asset)
    {
        if (!Enum.IsDefined(asset.NetworkMode) || !Enum.IsDefined(asset.OperatingSystem)) return false;
        if (asset.Kind == "docker" && asset.NetworkMode != TeamLabGuestNetworkMode.Dhcp) return false;
        if (asset.NetworkAttachments.Count(item => item.UseDefaultGateway ?? item.Primary) > 1) return false;
        if (asset.NetworkAttachments.Where(item => item.GuestInterfaceName is not null)
            .GroupBy(item => item.GuestInterfaceName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) return false;
        if (asset.NetworkMode == TeamLabGuestNetworkMode.ManagedStatic &&
            (asset.NetworkAttachments.GroupBy(item => item.InterfaceKey, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
             asset.NetworkAttachments.GroupBy(item => item.MacAddress, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))) return false;
        foreach (var item in asset.NetworkAttachments)
        {
            if (!IsInterfaceName(item.GuestInterfaceName) ||
                item.DnsServers is { } dns && (dns.Count > MaxDnsServers || dns.Any(value => !IsDnsServer(value)) ||
                    dns.Distinct(StringComparer.Ordinal).Count() != dns.Count) ||
                item.StaticRoutes is { } routes && (routes.Count > MaxStaticRoutes || routes.Any(route => !IsRoute(route)) ||
                    routes.GroupBy(route => route.DestinationCidr, StringComparer.Ordinal).Any(group => group.Count() > 1))) return false;
            if (asset.NetworkMode != TeamLabGuestNetworkMode.ManagedStatic &&
                (item.GuestInterfaceName is not null || item.StaticRoutes?.Any(route => route.Metric is not null) == true)) return false;
            if (asset.NetworkMode == TeamLabGuestNetworkMode.ManagedStatic &&
                (string.IsNullOrWhiteSpace(item.InterfaceKey) || item.PrefixLength is not (>= 1 and <= 30) ||
                 item.MacAddress is null || !Regex.IsMatch(item.MacAddress, "^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$") ||
                 !TryHost(item.IpAddress, out _))) return false;
            if (item.PrefixLength is { } prefix && item.StaticRoutes is { } staticRoutes &&
                staticRoutes.Any(route => !IsNextHopOnInterface(route.NextHop, item.IpAddress, prefix))) return false;
            if (asset.NetworkMode == TeamLabGuestNetworkMode.ManagedStatic && (item.UseDefaultGateway ?? item.Primary) &&
                !IsNextHopOnInterface(item.GatewayIp, item.IpAddress, item.PrefixLength!.Value)) return false;
            if (item.UseDefaultGateway == false && item.GatewayIp is not null) return false;
        }
        return true;
    }

    static bool TryHost(string? value, out IPAddress address)
    {
        address = IPAddress.None;
        return value is not null && value == value.Trim() && !value.Contains('/') &&
            IPAddress.TryParse(value, out address!) && address.AddressFamily == AddressFamily.InterNetwork &&
            value.Split('.').Length == 4 && value == address.ToString();
    }

    static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}
