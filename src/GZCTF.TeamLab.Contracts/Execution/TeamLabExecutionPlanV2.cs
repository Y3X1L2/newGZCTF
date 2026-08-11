using System.Security.Cryptography;
using System.Net;
using System.Text.Json;

namespace GZCTF.TeamLab.Contracts.Execution;

public sealed record TeamLabExecutionPlanV2(
    int RuntimeId,
    Guid RuntimePublicId,
    int Generation,
    string ShardKey,
    string PlanDigest,
    IReadOnlyList<TeamLabNetworkIntentV2> Networks,
    IReadOnlyList<TeamLabAssetExecutionSpecV2> Assets,
    IReadOnlyList<TeamLabArtifactReferenceV2> Artifacts,
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

        if (string.IsNullOrWhiteSpace(ShardKey) || !IsDigest(PlanDigest))
        {
            error = "Shard key and plan digest are required.";
            return false;
        }

        if (Networks is null || Assets is null || Artifacts is null || ObservationPoints is null ||
            Networks.GroupBy(item => item.Key, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            Assets.GroupBy(item => item.AssetKey, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            error = "Network keys and asset keys must be unique.";
            return false;
        }

        var networkKeys = Networks.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var networkPorts = Networks.SelectMany(network => network.Ports
                .Select(port => (network.Key, port.Key)))
            .ToHashSet();
        if (Networks.Any(network => string.IsNullOrWhiteSpace(network.Key) ||
                                    string.IsNullOrWhiteSpace(network.Cidr) ||
                                    network.DhcpLeases is { Count: > 0 } && string.IsNullOrWhiteSpace(network.GatewayIp) ||
                                    network.Ports.GroupBy(port => port.Key, StringComparer.Ordinal).Any(group => group.Count() != 1)) ||
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
                                asset.NetworkAttachments.Any(attachment =>
                                    !networkKeys.Contains(attachment.NetworkKey) ||
                                    !networkPorts.Contains((attachment.NetworkKey, attachment.PortKey)))))
        {
            error = "The execution plan contains an invalid network, attachment, asset kind, or image identity.";
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

        var assetKeys = Assets.Select(item => item.AssetKey).ToHashSet(StringComparer.Ordinal);
        if (Networks.SelectMany(item => item.Ports).Any(port =>
                string.IsNullOrWhiteSpace(port.Key) || !assetKeys.Contains(port.AssetKey) ||
                string.IsNullOrWhiteSpace(port.MacAddress)))
        {
            error = "Every network port must reference an asset in the same execution plan.";
            return false;
        }

        if (NetworkControl is { } control &&
            (control.RouteVersion < 0 ||
             control.Routers.GroupBy(router => router.Key, StringComparer.Ordinal)
                 .Any(group => group.Count() != 1) ||
             control.Routers.SelectMany(router => router.NetworkKeys)
                 .GroupBy(key => key, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            control.Routers.Any(router => string.IsNullOrWhiteSpace(router.Key)) ||
            control.Routers.Any(router => router.NetworkKeys.Any(key => !networkKeys.Contains(key))) ||
            control.Routers.SelectMany(router => router.NetworkKeys)
                .Select(key => Networks.First(network => network.Key == key))
                .Any(network => string.IsNullOrWhiteSpace(network.GatewayIp)) ||
            control.ForwardPolicies.Any(policy => string.IsNullOrWhiteSpace(policy.SourceCidr) ||
                                                    string.IsNullOrWhiteSpace(policy.DestinationCidr))))
        {
            error = "The network control intent contains an invalid router or forwarding policy.";
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
            : trimmed["sha256:".Length..].ToLowerInvariant();
    }
}

public sealed record TeamLabNetworkIntentV2(
    string Key,
    string Kind,
    string Cidr,
    string? GatewayIp,
    IReadOnlyList<TeamLabNetworkPortV2> Ports,
    IReadOnlyList<TeamLabNetworkRouteV2> Routes,
    IReadOnlyList<TeamLabNetworkPolicyV2> Policies,
    string? DhcpDnsServiceName = null,
    IReadOnlyList<TeamLabDhcpLeaseV2>? DhcpLeases = null,
    IReadOnlyList<TeamLabDnsRecordV2>? DnsRecords = null);

public sealed record TeamLabDhcpLeaseV2(
    string MacAddress,
    string IpAddress,
    string Hostname,
    bool IsPrimary = true);

public sealed record TeamLabDnsRecordV2(string Hostname, string IpAddress);

public sealed record TeamLabNetworkControlIntentV2(
    string RouterNamespace,
    int RouteVersion,
    IReadOnlyList<TeamLabRouterIntentV2> Routers,
    TeamLabFabricIntentV2? Fabric,
    IReadOnlyList<TeamLabForwardPolicyV2> ForwardPolicies);

public sealed record TeamLabRouterIntentV2(string Key, IReadOnlyList<string> NetworkKeys);

public sealed record TeamLabFabricIntentV2(
    string FabricIp,
    string HubAddressCidr,
    string NodeAddressCidr,
    string HostInterfaceName,
    string NamespaceInterfaceName,
    IReadOnlyList<TeamLabNetworkRouteV2> LocalRoutes,
    IReadOnlyList<TeamLabNetworkRouteV2> RemoteRoutes);

public sealed record TeamLabForwardPolicyV2(string SourceCidr, string DestinationCidr, bool Allow);

public sealed record TeamLabNetworkPortV2(
    string Key,
    string AssetKey,
    string MacAddress,
    string? IpAddress,
    bool IsPrimary);

public sealed record TeamLabNetworkRouteV2(
    string DestinationCidr,
    string? NextHop,
    string PortKey);

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
    bool IsPrimary);

public sealed record TeamLabHealthCheckV2(
    string Protocol,
    string Host,
    int Port,
    string? Path);

public sealed record TeamLabArtifactReferenceV2(
    string AssetKey,
    string Digest,
    string ArtifactType,
    long SizeBytes);

public sealed record TeamLabObservationIntentV2(
    Guid ObservationPointId,
    string AssetKey,
    string InterfaceToken,
    bool CaptureMetadata,
    bool CapturePackets);
