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
        TeamLabNodeInfrastructureApplyRequest infrastructure,
        IReadOnlyList<TeamLabNodeAssetCreateRequest> assets,
        IReadOnlyDictionary<int, string> imageDigests)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(imageDigests);

        var interfaceOwners = assets
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
            .Select(switchIntent => new TeamLabNetworkIntentV2(
                switchIntent.Network.Key,
                switchIntent.Network.Cidr,
                switchIntent.Network.GatewayIp,
                switchIntent.Records
                    .Where(record => interfaceOwners.ContainsKey(record.MacAddress))
                    .Select(record =>
                    {
                        var owner = interfaceOwners[record.MacAddress];
                        return new TeamLabNetworkPortV2(
                            owner.Interface.Key,
                            owner.AssetKey,
                            record.MacAddress,
                            AddressWithoutPrefix(record.IpAddress),
                            record.IsPrimary);
                    })
                    .Concat(assets.SelectMany(asset => asset.Interfaces
                        .Where(item => item.NetworkKey == switchIntent.Network.Key)
                        .Where(item => !switchIntent.Records.Any(record =>
                            record.MacAddress.Equals(item.MacAddress, StringComparison.OrdinalIgnoreCase)))
                        .Select(item => new TeamLabNetworkPortV2(
                            item.Key,
                            asset.AssetKey,
                            item.MacAddress,
                            AddressWithoutPrefix(item.IpAddress),
                            item.Primary))))
                    .DistinctBy(item => item.Key, StringComparer.Ordinal)
                    .ToArray(),
                Routes(infrastructure, switchIntent.Network.Key),
                Policies(infrastructure.ForwardPolicies, switchIntent.Network.Cidr),
                switchIntent.DhcpDnsServiceName,
                switchIntent.Records.Select(record => new TeamLabDhcpLeaseV2(
                    record.MacAddress,
                    AddressWithoutPrefix(record.IpAddress),
                    record.Hostname,
                    record.IsPrimary)).ToArray(),
                (switchIntent.DnsRecords ?? switchIntent.Records)
                    .Select(record => new TeamLabDnsRecordV2(record.Hostname, AddressWithoutPrefix(record.IpAddress)))
                    .DistinctBy(record => (record.Hostname, record.IpAddress))
                    .ToArray()))
            .ToArray();

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
                    ? TeamLabExecutionIdentityV2.VmDomainName(runtimePublicId, generation, asset.AssetKey)
                    : asset.AssetKey,
                digest,
                asset.Kind == TeamLabAssetKind.Vm
                    ? TeamLabExecutionIdentityV2.VmDomainName(runtimePublicId, generation, asset.AssetKey)
                    : null,
                asset.ImageTemplateId,
                asset.CpuUnits,
                asset.MemoryMiB,
                asset.Interfaces.Select(item => new TeamLabAssetNetworkAttachmentV2(
                    item.NetworkKey,
                    item.Key,
                    item.Key,
                    AddressWithoutPrefix(item.IpAddress),
                    item.Primary)).ToArray(),
                health is not null
                    ? [new TeamLabHealthCheckV2(
                        health.Kind == TeamLabHealthCheckKind.Http ? "http" : "tcp",
                        AddressWithoutPrefix(primary?.IpAddress ?? "127.0.0.1"),
                        health.Port!.Value,
                        health.Kind == TeamLabHealthCheckKind.Http ? "/" : null)]
                    : [],
                asset.ImageReference);
        }).ToArray();

        var observations = infrastructure.ObservationPoints
            .Select(point => new TeamLabObservationIntentV2(
                point.PublicId,
                point.TopologyKey,
                point.InterfaceToken,
                CaptureMetadata: true,
                CapturePackets: false))
            .ToArray();
        var control = new TeamLabNetworkControlIntentV2(
            infrastructure.Routers.Select(router => new TeamLabRouterIntentV2(
                router.Key, router.NetworkKeys)).ToArray(),
            infrastructure.ForwardPolicies.Select(policy => new TeamLabForwardPolicyV2(
                policy.SourceCidr, policy.DestinationCidr, policy.Allow)).ToArray());

        var plan = new TeamLabExecutionPlanV2(
            runtimeId,
            runtimePublicId,
            generation,
            shardKey,
            string.Empty,
            networks,
            executionAssets,
            observations,
            control);
        var digest = ComputeDigest(plan);
        return plan with { PlanDigest = $"sha256:{digest}" };
    }

    static string ComputeDigest(TeamLabExecutionPlanV2 plan) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            plan with { PlanDigest = string.Empty }))).ToLowerInvariant();

    static TeamLabNetworkRouteV2[] Routes(
        TeamLabNodeInfrastructureApplyRequest infrastructure, string networkKey) =>
        infrastructure.Fabric.LocalRoutes.Concat(infrastructure.Fabric.RemoteRoutes)
            .Where(route => !string.IsNullOrWhiteSpace(route.TargetCidr))
            .Select(route => new TeamLabNetworkRouteV2(
                route.TargetCidr, route.GatewayIp))
            .DistinctBy(route => (route.DestinationCidr, route.NextHop))
            .ToArray();

    static TeamLabNetworkPolicyV2[] Policies(
        IReadOnlyList<TeamLabNodeForwardPolicy> policies, string networkCidr) =>
        policies.Where(policy => policy.SourceCidr == networkCidr || policy.DestinationCidr == networkCidr)
            .Select(policy => new TeamLabNetworkPolicyV2(
                policy.SourceCidr, policy.DestinationCidr, "any", null, policy.Allow))
            .ToArray();

    static string AddressWithoutPrefix(string address) => address.Split('/', 2)[0];

    sealed record InterfaceOwner(string MacAddress, string AssetKey, TeamLabNodeInterfaceIntent Interface);

}
