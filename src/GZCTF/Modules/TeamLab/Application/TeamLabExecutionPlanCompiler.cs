using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.Content.Application;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Converts the already validated node intents into the transport plan consumed by an Agent.
/// It deliberately accepts application intents rather than database entities, keeping the
/// execution contract independent from the TeamLab authoring and persistence models.
/// </summary>
public static class TeamLabExecutionPlanCompiler
{
    public static TeamLabExecutionPlanV2 Compile(
        int runtimeId,
        Guid runtimePublicId,
        int generation,
        string shardKey,
        bool networkOwner,
        TeamLabNodeInfrastructureApplyRequest infrastructure,
        IReadOnlyList<TeamLabNodeAssetCreateRequest> allAssets,
        IReadOnlyList<TeamLabNodeAssetCreateRequest> assets,
        IReadOnlyList<TeamLabNodeObservationPointIntent> observations,
        IReadOnlyDictionary<int, string> imageDigests)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(imageDigests);

        var interfaceOwners = allAssets
            .SelectMany(asset => asset.Interfaces.Select(item => new InterfaceOwner(
                item.MacAddress, asset.AssetKey, item)))
            .GroupBy(item => item.MacAddress, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException(
                        $"Network MAC address {group.Key} is assigned to more than one asset."),
                StringComparer.OrdinalIgnoreCase);

        var networks = infrastructure.Switches
            .Select(switchIntent =>
            {
                var networkPorts = switchIntent.Records
                    .Where(record => interfaceOwners.ContainsKey(record.MacAddress))
                    .Select(record =>
                    {
                        var owner = interfaceOwners[record.MacAddress];
                        return new TeamLabNetworkPortV2(
                            owner.Interface.Key,
                            owner.AssetKey,
                            record.MacAddress,
                            AddressWithoutPrefix(record.IpAddress));
                    })
                    .Concat(allAssets.SelectMany(asset => asset.Interfaces
                        .Where(item => item.NetworkKey == switchIntent.Network.Key)
                        .Where(item => !switchIntent.Records.Any(record =>
                            record.MacAddress.Equals(item.MacAddress, StringComparison.OrdinalIgnoreCase)))
                        .Select(item => new TeamLabNetworkPortV2(
                            item.Key,
                            asset.AssetKey,
                            item.MacAddress,
                            AddressWithoutPrefix(item.IpAddress)))))
                    .DistinctBy(item => item.Key, StringComparer.Ordinal)
                    .ToArray();
                var playerGateway = switchIntent.Network.IsEntry
                    ? PlayerGateway(runtimeId, runtimePublicId, generation, switchIntent.Network, networkPorts)
                    : null;
                return new TeamLabNetworkIntentV2(
                    switchIntent.Network.Key,
                    switchIntent.Network.Cidr,
                    switchIntent.Network.GatewayIp,
                    networkPorts,
                    [],
                    Policies(infrastructure.ForwardPolicies, switchIntent.Network.Cidr),
                    switchIntent.DhcpDnsServiceName,
                    switchIntent.Records.Select(record => new TeamLabDhcpLeaseV2(
                        record.MacAddress,
                        AddressWithoutPrefix(record.IpAddress),
                        record.Hostname)).ToArray(),
                    (switchIntent.DnsRecords ?? switchIntent.Records)
                        .Select(record => new TeamLabDnsRecordV2(record.Hostname, AddressWithoutPrefix(record.IpAddress)))
                        .DistinctBy(record => (record.Hostname, record.IpAddress))
                        .ToArray(),
                    playerGateway);
            })
            .ToArray();

        var gateways = infrastructure.Switches
            .ToDictionary(
                switchIntent => switchIntent.Network.Key,
                switchIntent => switchIntent.Network.GatewayIp ?? string.Empty,
                StringComparer.Ordinal);
        var executionAssets = assets.Select(asset =>
        {
            if (!imageDigests.TryGetValue(asset.ImageTemplateId, out var digest) ||
                string.IsNullOrWhiteSpace(digest))
                throw new InvalidOperationException($"Image digest is missing for template {asset.ImageTemplateId}.");
            var health = asset.Health;
            if (health is not null && health.Port is not (> 0 and <= 65535))
                throw new InvalidOperationException($"Health check port is required and must be valid for asset {asset.AssetKey}.");
            var primary = asset.Interfaces.FirstOrDefault(item => item.Primary) ?? asset.Interfaces.FirstOrDefault();
            return new TeamLabAssetExecutionSpecV2(
                asset.AssetKey,
                asset.Kind == TeamLabAssetKind.Docker ? "docker" : "vm",
                asset.Kind == TeamLabAssetKind.Vm
                    ? TeamLabExecutionIdentityV2.VmDomainName(runtimePublicId, generation, shardKey, asset.AssetKey)
                    : asset.AssetKey,
                digest,
                asset.Kind == TeamLabAssetKind.Vm
                    ? TeamLabExecutionIdentityV2.VmDomainName(runtimePublicId, generation, shardKey, asset.AssetKey)
                    : null,
                asset.ImageTemplateId,
                asset.CpuUnits,
                asset.MemoryMiB,
                NetworkAttachments(asset, gateways),
                health is not null
                    ? [new TeamLabHealthCheckV2(
                        health.Kind == TeamLabHealthCheckKind.Http ? "http" : "tcp",
                        AddressWithoutPrefix(primary?.IpAddress ?? "127.0.0.1"),
                        health.Port!.Value,
                        health.Kind == TeamLabHealthCheckKind.Http ? "/" : null)]
                    : [],
                asset.ImageReference);
        }).ToArray();

        var observationIntents = observations
            .Where(point => point.Kind == TeamLabObservationPointKind.WorkloadEndpoint &&
                            !string.IsNullOrWhiteSpace(point.NetworkKey))
            .Select(point =>
            {
                var isVm = assets.Any(asset =>
                    string.Equals(asset.AssetKey, point.TopologyKey, StringComparison.Ordinal) &&
                    asset.Kind == TeamLabAssetKind.Vm);
                return new TeamLabObservationIntentV2(
                    point.PublicId,
                    point.TopologyKey,
                    isVm
                        ? TeamLabExecutionIdentityV2.VmTapName(
                            runtimePublicId, generation, point.TopologyKey, point.NetworkKey!)
                        : TeamLabExecutionIdentityV2.WorkloadHostInterface(
                            runtimePublicId, generation, point.TopologyKey, point.NetworkKey!),
                    CaptureMetadata: true);
            })
            .ToArray();
        var control = new TeamLabNetworkControlIntentV2(
            infrastructure.Routers.Select(router => new TeamLabRouterIntentV2(
                router.Key, router.NetworkKeys)).ToArray(),
            infrastructure.ForwardPolicies.Select(policy => new TeamLabForwardPolicyV2(
                policy.SourceCidr, policy.DestinationCidr, policy.Allow)).ToArray());

        var networkDigest = ComputeNetworkDigest(networks, control);
        var plan = new TeamLabExecutionPlanV2(
            runtimeId,
            runtimePublicId,
            generation,
            shardKey,
            string.Empty,
            networkDigest,
            networkOwner,
            networks,
            executionAssets,
            observationIntents,
            control);
        var digest = ComputeDigest(plan);
        return plan with { PlanDigest = $"sha256:{digest}" };
    }

    static string ComputeDigest(TeamLabExecutionPlanV2 plan) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            plan with { PlanDigest = string.Empty }))).ToLowerInvariant();

    static string ComputeNetworkDigest(
        IReadOnlyList<TeamLabNetworkIntentV2> networks,
        TeamLabNetworkControlIntentV2 control) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { networks, control }))).ToLowerInvariant()}";

    static TeamLabNetworkPolicyV2[] Policies(
        IReadOnlyList<TeamLabNodeForwardPolicy> policies, string networkCidr) =>
        policies.Where(policy => policy.SourceCidr == networkCidr || policy.DestinationCidr == networkCidr)
            .Select(policy => new TeamLabNetworkPolicyV2(
                policy.SourceCidr, policy.DestinationCidr, "any", null, policy.Allow))
            .ToArray();

    static TeamLabAssetNetworkAttachmentV2[] NetworkAttachments(
        TeamLabNodeAssetCreateRequest asset,
        IReadOnlyDictionary<string, string> gateways) =>
        asset.Interfaces
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select((item, index) => new TeamLabAssetNetworkAttachmentV2(
                item.NetworkKey,
                item.Key,
                $"eth{index}",
                AddressWithoutPrefix(item.IpAddress),
                gateways.GetValueOrDefault(item.NetworkKey),
                item.Primary))
            .ToArray();

    static string AddressWithoutPrefix(string address) => address.Split('/', 2)[0];

    static TeamLabPlayerGatewayV2 PlayerGateway(
        int runtimeId,
        Guid runtimePublicId,
        int generation,
        TeamLabNodeNetworkIntent network,
        IReadOnlyList<TeamLabNetworkPortV2> ports)
    {
        if (string.IsNullOrWhiteSpace(network.GatewayIp))
            throw new InvalidOperationException($"Player entry network {network.Key} has no gateway.");
        var used = ports.Select(port => port.IpAddress)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Append(network.GatewayIp)
            .ToHashSet(StringComparer.Ordinal);
        var ip = LastHost(network.Cidr);
        if (used.Contains(ip))
            throw new InvalidOperationException(
                $"Player entry network {network.Key} already uses the gateway host address {ip}.");
        return new TeamLabPlayerGatewayV2(
            "player-gateway",
            PlayerGatewayMac(runtimePublicId, generation, network.Key),
            ip,
            TeamLabResourceNameFactory.WireGuardInterface(runtimeId));
    }

    static string PlayerGatewayMac(Guid runtimePublicId, int generation, string networkKey) =>
        TeamLabResourceNameFactory.PlayerGatewayMac(runtimePublicId, generation, networkKey);

    static string LastHost(string cidr)
    {
        var parts = cidr.Split('/', 2);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) ||
            prefix is < 2 or > 30)
            throw new InvalidOperationException($"Network CIDR has no valid prefix: {cidr}");
        var hostBits = 32 - prefix;
        var networkValue = BitConverter.ToUInt32(address.GetAddressBytes().Reverse().ToArray());
        var lastHost = networkValue + ((1u << hostBits) - 1) - 1;
        var bytes = BitConverter.GetBytes(lastHost).Reverse().ToArray();
        return string.Join('.', bytes.Select(value => value.ToString()));
    }
    sealed record InterfaceOwner(string MacAddress, string AssetKey, TeamLabNodeInterfaceIntent Interface);

}
