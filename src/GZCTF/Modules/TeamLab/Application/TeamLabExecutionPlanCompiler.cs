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
        IReadOnlyDictionary<int, string> imageDigests,
        IReadOnlyDictionary<string, IReadOnlyList<TeamLabConnectorAttachmentV2>>? connectors = null)
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
        var interfacesByNetwork = allAssets
            .SelectMany(asset => asset.Interfaces.Select(item => new InterfaceOwner(
                item.MacAddress, asset.AssetKey, item)))
            .GroupBy(item => item.Interface.NetworkKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var policiesByCidr = infrastructure.ForwardPolicies
            .SelectMany(policy => new[] { policy.SourceCidr, policy.DestinationCidr }
                .Distinct(StringComparer.Ordinal)
                .Select(cidr => new { Cidr = cidr, Policy = policy }))
            .GroupBy(item => item.Cidr, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(item => item.Policy).ToArray(), StringComparer.Ordinal);

        var networks = infrastructure.Switches
            .Select(switchIntent =>
            {
                var recordedMacs = switchIntent.Records
                    .Select(record => record.MacAddress)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var networkPorts = switchIntent.Records
                    .Where(record => interfaceOwners.ContainsKey(record.MacAddress))
                    .Select(record =>
                    {
                        var owner = interfaceOwners[record.MacAddress];
                        return new TeamLabNetworkPortV2(
                            PortKey(owner.AssetKey, owner.Interface.Key),
                            owner.AssetKey,
                            record.MacAddress,
                            AddressWithoutPrefix(record.IpAddress));
                    })
                    .Concat(interfacesByNetwork.GetValueOrDefault(switchIntent.Network.Key, [])
                        .Where(owner => !recordedMacs.Contains(owner.MacAddress))
                        .Select(owner => new TeamLabNetworkPortV2(
                            PortKey(owner.AssetKey, owner.Interface.Key),
                            owner.AssetKey,
                            owner.MacAddress,
                            AddressWithoutPrefix(owner.Interface.IpAddress))))
                    .DistinctBy(item => item.Key, StringComparer.Ordinal)
                    .ToArray();
                var playerGateway = switchIntent.Network.IsEntry
                    ? PlayerGateway(runtimeId, runtimePublicId, generation, switchIntent.Network, networkPorts)
                    : null;
                var serviceGateway = ServiceGateway(
                    runtimeId, runtimePublicId, generation, switchIntent.Network, networkPorts, playerGateway);
                return new TeamLabNetworkIntentV2(
                    switchIntent.Network.Key,
                    switchIntent.Network.Cidr,
                    switchIntent.Network.GatewayIp,
                    networkPorts,
                    [],
                    (policiesByCidr.GetValueOrDefault(switchIntent.Network.Cidr) ?? [])
                        .Select(policy => new TeamLabNetworkPolicyV2(
                            policy.SourceCidr, policy.DestinationCidr, "any", null, policy.Allow))
                        .ToArray(),
                    switchIntent.DhcpDnsServiceName,
                    switchIntent.Records.Select(record => new TeamLabDhcpLeaseV2(
                        record.MacAddress,
                        AddressWithoutPrefix(record.IpAddress),
                        record.Hostname)).ToArray(),
                    (switchIntent.DnsRecords ?? switchIntent.Records)
                        .Select(record => new TeamLabDnsRecordV2(record.Hostname, AddressWithoutPrefix(record.IpAddress)))
                        .DistinctBy(record => (record.Hostname, record.IpAddress))
                        .ToArray(),
                    playerGateway,
                    connectors?.GetValueOrDefault(switchIntent.Network.Key),
                    serviceGateway);
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
                (health is not null
                    ? new[] { new TeamLabHealthCheckV2(
                        health.Kind == TeamLabHealthCheckKind.Http ? "http" : "tcp",
                        AddressWithoutPrefix(primary?.IpAddress ?? "127.0.0.1"),
                        health.Port!.Value,
                        health.Kind == TeamLabHealthCheckKind.Http ? "/" : null) }
                    : []).Concat(asset.Device is { HealthProtocol: not null, HealthPort: not null } device
                        ? [new TeamLabHealthCheckV2(device.HealthProtocol, AddressWithoutPrefix(primary?.IpAddress ?? "127.0.0.1"), device.HealthPort.Value, device.HealthPath)]
                        : []).Distinct().ToArray(),
                asset.ImageReference,
                asset.Device);
        }).ToArray();

        var assetKinds = assets.ToDictionary(item => item.AssetKey, item => item.Kind, StringComparer.Ordinal);
        var observationIntents = observations
            .Where(point => point.Kind == TeamLabObservationPointKind.WorkloadEndpoint &&
                            !string.IsNullOrWhiteSpace(point.NetworkKey))
            .Select(point =>
            {
                var isVm = assetKinds.GetValueOrDefault(point.TopologyKey) == TeamLabAssetKind.Vm;
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

    static TeamLabAssetNetworkAttachmentV2[] NetworkAttachments(
        TeamLabNodeAssetCreateRequest asset,
        IReadOnlyDictionary<string, string> gateways) =>
        asset.Interfaces
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select((item, index) => new TeamLabAssetNetworkAttachmentV2(
                item.NetworkKey,
                PortKey(asset.AssetKey, item.Key),
                $"eth{index}",
                AddressWithoutPrefix(item.IpAddress),
                gateways.GetValueOrDefault(item.NetworkKey),
                item.Primary))
            .ToArray();

    static string PortKey(string assetKey, string interfaceKey) => $"{assetKey}:{interfaceKey}";

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

    static TeamLabPlayerGatewayV2 ServiceGateway(
        int runtimeId,
        Guid runtimePublicId,
        int generation,
        TeamLabNodeNetworkIntent network,
        IReadOnlyList<TeamLabNetworkPortV2> ports,
        TeamLabPlayerGatewayV2? playerGateway)
    {
        var ip = LastHost(network.Cidr, network.IsEntry ? 2 : 1);
        if (ports.Any(port => string.Equals(port.IpAddress, ip, StringComparison.Ordinal)) ||
            string.Equals(network.GatewayIp, ip, StringComparison.Ordinal) ||
            string.Equals(playerGateway?.IpAddress, ip, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"TeamLab network {network.Key} already uses the service gateway address {ip}.");
        return new TeamLabPlayerGatewayV2(
            "service-gateway",
            TeamLabResourceNameFactory.ServiceGatewayMac(runtimePublicId, generation, network.Key),
            ip,
            TeamLabResourceNameFactory.ServiceGatewayInterface(runtimeId, network.Key));
    }

    static string PlayerGatewayMac(Guid runtimePublicId, int generation, string networkKey) =>
        TeamLabResourceNameFactory.PlayerGatewayMac(runtimePublicId, generation, networkKey);

    static string LastHost(string cidr, int offset = 1)
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
        var lastHost = networkValue + ((1u << hostBits) - 1) - (uint)offset;
        var bytes = BitConverter.GetBytes(lastHost).Reverse().ToArray();
        return string.Join('.', bytes.Select(value => value.ToString()));
    }
    sealed record InterfaceOwner(string MacAddress, string AssetKey, TeamLabNodeInterfaceIntent Interface);

}
