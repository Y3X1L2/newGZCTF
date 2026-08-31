using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace GZCTF.TeamLab.Contracts.Execution;

public sealed record TeamLabExecutionPlanV2(
    int RuntimeId,
    Guid RuntimePublicId,
    int Generation,
    string ShardKey,
    string PlanDigest,
    string NetworkDigest,
    bool NetworkOwner,
    IReadOnlyList<TeamLabNetworkIntentV2> Networks,
    IReadOnlyList<TeamLabAssetExecutionSpecV2> Assets,
    IReadOnlyList<TeamLabObservationIntentV2> ObservationPoints,
    TeamLabNetworkControlIntentV2? NetworkControl = null)
{
    public bool IsValid(out string? error)
    {
        if (RuntimeId <= 0 || RuntimePublicId == Guid.Empty || Generation <= 0)
        {
            error = "Runtime identity is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ShardKey) || !IsDigest(PlanDigest) || !IsDigest(NetworkDigest))
        {
            error = "Shard key, network digest and plan digest are required.";
            return false;
        }

        if (Networks is null || Assets is null || ObservationPoints is null ||
            Networks.GroupBy(item => item.Key, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            Assets.GroupBy(item => item.AssetKey, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            error = "Network keys and asset keys must be unique.";
            return false;
        }

        if (Networks.Any(network => network.Ports is null || network.Routes is null || network.Policies is null) ||
            Assets.Any(asset => asset.NetworkAttachments is null || asset.HealthChecks is null))
        {
            error = "The execution plan contains a missing collection.";
            return false;
        }

        if (Networks.Any(network => network.DnsRecords is { } records && records
                .GroupBy(record => record.Hostname, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1)))
        {
            error = "DNS hostnames must be unique within each network.";
            return false;
        }

        var networkKeys = Networks.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var networkPortRecords = Networks.SelectMany(network => network.Ports
            .Select(port => (NetworkKey: network.Key, PortKey: port.Key, port.AssetKey, port.IpAddress)))
            .ToArray();
        if (networkPortRecords.GroupBy(item => (item.NetworkKey, item.PortKey)).Any(group => group.Count() != 1))
        {
            error = "Network port identities must be unique within a network.";
            return false;
        }
        var networkPorts = networkPortRecords.ToDictionary(
            item => (item.NetworkKey, item.PortKey), item => (item.AssetKey, item.IpAddress));
        if (Networks.Any(network => string.IsNullOrWhiteSpace(network.Key) ||
                                    !TryParseIpv4Cidr(network.Cidr, out var networkAddress, out var prefixLength) ||
                                    !IsAddressInCidr(network.GatewayIp, networkAddress, prefixLength) ||
                                    network.DhcpLeases is { Count: > 0 } && string.IsNullOrWhiteSpace(network.GatewayIp) ||
                                    network.Ports.GroupBy(port => port.Key, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
                                    network.Routes.GroupBy(route => (route.DestinationCidr, route.NextHop))
                                        .Any(group => group.Count() != 1) ||
                                    network.Policies.GroupBy(policy => (policy.SourceCidr, policy.DestinationCidr,
                                            policy.Protocol, policy.Port, policy.Allow))
                                        .Any(group => group.Count() != 1) ||
                                    network.Ports.Any(port => !IsMacAddress(port.MacAddress) ||
                                                                 !IsAddressInCidr(port.IpAddress, networkAddress, prefixLength)) ||
                                    network.Routes.Any(route => !TryParseIpv4Cidr(route.DestinationCidr, out _, out _) ||
                                                                !string.IsNullOrWhiteSpace(route.NextHop) &&
                                                                !TryParseIpv4Address(route.NextHop)) ||
                                    network.Policies.Any(policy => !IsValidPolicy(policy)) ||
                                    network.DhcpLeases is { } leases && leases.Any(lease =>
                                        !IsMacAddress(lease.MacAddress) ||
                                        !IsAddressInCidr(lease.IpAddress, networkAddress, prefixLength) ||
                                        !IsValidHostname(lease.Hostname)) ||
                                    network.DhcpLeases is { } duplicateLeases && duplicateLeases
                                        .GroupBy(lease => $"{lease.MacAddress}\u0001{lease.IpAddress}",
                                            StringComparer.OrdinalIgnoreCase)
                                        .Any(group => group.Count() != 1) ||
                                    network.DnsRecords is { } records && records.Any(record =>
                                        !IsValidHostname(record.Hostname) || !TryParseIpv4Address(record.IpAddress)) ||
                                    network.DnsRecords is { } duplicateRecords && duplicateRecords
                                        .GroupBy(record => (record.Hostname, record.IpAddress))
                                        .Any(group => group.Count() != 1)) ||
            Assets.Any(asset => !asset.Kind.Equals("docker", StringComparison.OrdinalIgnoreCase) &&
                                !asset.Kind.Equals("vm", StringComparison.OrdinalIgnoreCase) ||
                                string.IsNullOrWhiteSpace(asset.ImageDigest) ||
                                !IsDigest(asset.ImageDigest) ||
                                asset.HealthChecks is null ||
                                asset.HealthChecks.Any(check =>
                                    (!check.Protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase) &&
                                     !check.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase)) ||
                                    !IPAddress.TryParse(check.Host, out _) ||
                                    check.Port is < 1 or > 65535 ||
                                    (check.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                                     !string.IsNullOrEmpty(check.Path) && !check.Path.StartsWith('/'))) ||
                                asset.NetworkAttachments.GroupBy(attachment => (attachment.NetworkKey, attachment.PortKey))
                                    .Any(group => group.Count() != 1) ||
                                asset.NetworkAttachments.Any(attachment =>
                                    !networkKeys.Contains(attachment.NetworkKey) ||
                                    !networkPorts.TryGetValue((attachment.NetworkKey, attachment.PortKey), out var port) ||
                                    !string.Equals(port.AssetKey, asset.AssetKey, StringComparison.Ordinal) ||
                                    !IsSameIpv4Address(port.IpAddress, attachment.IpAddress) ||
                                    !TryParseIpv4Address(attachment.IpAddress))))
        {
            error = "The execution plan contains an invalid network, attachment, asset kind, or image identity.";
            return false;
        }

        if (Networks.Any(network => network.PlayerGateway is { } gateway &&
            (string.IsNullOrWhiteSpace(gateway.PortKey) ||
             !IsMacAddress(gateway.MacAddress) ||
             !TryParseIpv4Cidr(network.Cidr, out var gatewayNetwork, out var gatewayPrefix) ||
             !IsAddressInCidr(gateway.IpAddress, gatewayNetwork, gatewayPrefix) ||
             network.Ports.Any(port => IsSameIpv4Address(port.IpAddress, gateway.IpAddress)) ||
             string.IsNullOrWhiteSpace(gateway.InterfaceName))))
        {
            error = "The execution plan contains an invalid player gateway.";
            return false;
        }

        if (Assets.Any(item => string.IsNullOrWhiteSpace(item.AssetKey) ||
                               item.AssetKey.Length > 128 ||
                               item.AssetKey.Contains('/') ||
                               item.AssetKey.Contains('\\') ||
                               item.AssetKey.Contains("..", StringComparison.Ordinal) ||
                               string.IsNullOrWhiteSpace(item.ResourceId)))
        {
            error = "Every asset requires an asset key and resource identity.";
            return false;
        }

        // Multi-shard plans intentionally carry the complete logical network (including ports
        // owned by peer shards) so the network owner can build the global OVN topology and
        // non-owner shards can wait for it. Local asset attachments are still validated above;
        // ports whose asset is not in this shard's Assets are remote/peer ports and are allowed.
        if (Networks.SelectMany(item => item.Ports).Any(port =>
                string.IsNullOrWhiteSpace(port.Key) ||
                string.IsNullOrWhiteSpace(port.MacAddress)) ||
            Networks.Any(network => network.Ports.Any(port =>
                network.DhcpLeases is { } leases && !leases.Any(lease =>
                    string.Equals(lease.MacAddress, port.MacAddress, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(lease.IpAddress, port.IpAddress, StringComparison.OrdinalIgnoreCase)))))
        {
            error = "Every network port must have a valid key, MAC address, and DHCP binding.";
            return false;
        }

        if (NetworkControl is { } control &&
            ((control.Routers is null || control.ForwardPolicies is null) ||
             (Networks.Count == 0 && (control.Routers.Count > 0 || control.ForwardPolicies.Count > 0)) ||
             control.Routers.GroupBy(router => router.Key, StringComparer.Ordinal)
                 .Any(group => group.Count() != 1) ||
             control.Routers.SelectMany(router => router.NetworkKeys)
                 .GroupBy(key => key, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            control.Routers.Any(router => string.IsNullOrWhiteSpace(router.Key) ||
                                           !IsSafeKey(router.Key)) ||
            control.Routers.Any(router => router.NetworkKeys.Any(key => !networkKeys.Contains(key))) ||
            control.Routers.SelectMany(router => router.NetworkKeys)
                .Select(key => Networks.First(network => network.Key == key))
                .Any(network => string.IsNullOrWhiteSpace(network.GatewayIp)) ||
            control.ForwardPolicies.GroupBy(policy => (policy.SourceCidr, policy.DestinationCidr, policy.Allow))
                .Any(group => group.Count() != 1) ||
            control.ForwardPolicies.Any(policy => !TryParseIpv4Cidr(policy.SourceCidr, out _, out _) ||
                                                    !TryParseIpv4Cidr(policy.DestinationCidr, out _, out _))))
        {
            error = "The network control intent contains an invalid router or forwarding policy.";
            return false;
        }

        if (Assets.Any(asset => asset.TemplateId <= 0))
        {
            error = "Every asset requires a source template identity.";
            return false;
        }

        var expectedDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            this with { PlanDigest = string.Empty }))).ToLowerInvariant();
        if (!string.Equals(NormalizeDigest(PlanDigest), expectedDigest, StringComparison.Ordinal))
        {
            error = "Plan digest does not match the execution plan contents.";
            return false;
        }

        error = null;
        return true;
    }

    static bool IsDigest(string value)
    {
        var trimmed = value.Trim();
        var marker = trimmed.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        var digest = marker >= 0
            ? trimmed[(marker + "@sha256:".Length)..]
            : trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? trimmed["sha256:".Length..]
                : string.Empty;
        return digest.Length == 64 && digest.All(Uri.IsHexDigit);
    }

    static string NormalizeDigest(string value)
    {
        var trimmed = value.Trim();
        var marker = trimmed.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        return marker >= 0
            ? trimmed[(marker + "@sha256:".Length)..].ToLowerInvariant()
            : trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? trimmed["sha256:".Length..].ToLowerInvariant()
                : trimmed.ToLowerInvariant();
    }

    static bool IsValidPolicy(TeamLabNetworkPolicyV2 policy) =>
        TryParseIpv4Cidr(policy.SourceCidr, out _, out _) &&
        TryParseIpv4Cidr(policy.DestinationCidr, out _, out _) &&
        !string.IsNullOrWhiteSpace(policy.Protocol) &&
        policy.Port is null or >= 1 and <= 65535 &&
        policy.Protocol.Trim().ToLowerInvariant() switch
        {
            "any" or "tcp" or "udp" or "icmp" => policy.Port is null ||
                policy.Protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase) ||
                policy.Protocol.Equals("udp", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    static bool IsSafeKey(string value) => value.Length <= 128 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    static bool IsMacAddress(string? value) => !string.IsNullOrWhiteSpace(value) &&
        System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$");

    static bool IsValidHostname(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 253 &&
        value.Split('.', StringSplitOptions.RemoveEmptyEntries).All(label => label.Length is > 0 and <= 63 &&
            label.All(character => char.IsLetterOrDigit(character) || character == '-') && label[0] != '-' && label[^1] != '-');

    static bool TryParseIpv4Address(string? value) => TryParseIpv4HostAddress(value, out _);

    static bool IsSameIpv4Address(string? left, string? right) =>
        TryParseIpv4HostAddress(left, out var leftAddress) &&
        TryParseIpv4HostAddress(right, out var rightAddress) &&
        leftAddress.Equals(rightAddress);

    static bool TryParseIpv4HostAddress(string? value, out IPAddress address)
    {
        address = IPAddress.None;
        var host = value?.Split('/', 2)[0];
        if (string.IsNullOrWhiteSpace(host) || !IPAddress.TryParse(host, out var parsed) ||
            parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;
        address = parsed;
        return true;
    }

    static bool TryParseIpv4Cidr(string? value, out IPAddress address, out int prefixLength)
    {
        address = IPAddress.None;
        prefixLength = -1;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/', 2);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var parsed) ||
            parsed.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out prefixLength) ||
            prefixLength is not (> 0 and < 31))
            return false;
        address = parsed;
        return true;
    }

    static bool IsAddressInCidr(string? value, IPAddress network, int prefixLength)
    {
        if (!TryParseIpv4HostAddress(value, out var address)) return false;
        var networkValue = BitConverter.ToUInt32(network.GetAddressBytes().Reverse().ToArray());
        var addressValue = BitConverter.ToUInt32(address.GetAddressBytes().Reverse().ToArray());
        var mask = uint.MaxValue << (32 - prefixLength);
        return (networkValue & mask) == (addressValue & mask);
    }
}

public sealed record TeamLabNetworkIntentV2(
    string Key,
    string Cidr,
    string? GatewayIp,
    IReadOnlyList<TeamLabNetworkPortV2> Ports,
    IReadOnlyList<TeamLabNetworkRouteV2> Routes,
    IReadOnlyList<TeamLabNetworkPolicyV2> Policies,
    string? DhcpDnsServiceName = null,
    IReadOnlyList<TeamLabDhcpLeaseV2>? DhcpLeases = null,
    IReadOnlyList<TeamLabDnsRecordV2>? DnsRecords = null,
    TeamLabPlayerGatewayV2? PlayerGateway = null);

public sealed record TeamLabPlayerGatewayV2(
    string PortKey,
    string MacAddress,
    string IpAddress,
    string InterfaceName);
public sealed record TeamLabDhcpLeaseV2(
    string MacAddress,
    string IpAddress,
    string Hostname);

public sealed record TeamLabDnsRecordV2(string Hostname, string IpAddress);

public sealed record TeamLabNetworkControlIntentV2(
    IReadOnlyList<TeamLabRouterIntentV2> Routers,
    IReadOnlyList<TeamLabForwardPolicyV2> ForwardPolicies);

public sealed record TeamLabRouterIntentV2(string Key, IReadOnlyList<string> NetworkKeys);

public sealed record TeamLabForwardPolicyV2(string SourceCidr, string DestinationCidr, bool Allow);

public sealed record TeamLabNetworkPortV2(
    string Key,
    string AssetKey,
    string MacAddress,
    string? IpAddress);

public sealed record TeamLabNetworkRouteV2(
    string DestinationCidr,
    string? NextHop);

public sealed record TeamLabNetworkPolicyV2(
    string SourceCidr,
    string DestinationCidr,
    string Protocol,
    int? Port,
    bool Allow);

public sealed record TeamLabAssetExecutionSpecV2(
    string AssetKey,
    string Kind,
    string ResourceId,
    string ImageDigest,
    string? DomainIdentity,
    int TemplateId,
    int Cpu,
    int MemoryMiB,
    IReadOnlyList<TeamLabAssetNetworkAttachmentV2> NetworkAttachments,
    IReadOnlyList<TeamLabHealthCheckV2> HealthChecks,
    string? ImageReference = null);

public sealed record TeamLabAssetNetworkAttachmentV2(
    string NetworkKey,
    string PortKey,
    string InterfaceName,
    string? IpAddress,
    string? GatewayIp = null,
    bool Primary = false);

public sealed record TeamLabHealthCheckV2(
    string Protocol,
    string Host,
    int Port,
    string? Path);

public sealed record TeamLabObservationIntentV2(
    Guid ObservationPointId,
    string AssetKey,
    string InterfaceToken,
    bool CaptureMetadata);
