using System.Security.Cryptography;
using System.Text;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public static class TeamLabTopologyV1Normalizer
{
    public static TeamLabExecutionTopology Normalize(TeamLabTopologyDefinitionModel definition)
    {
        var networks = definition.Networks.Select(ToExecution).ToArray();
        var assets = definition.Assets.Select(ToExecution).ToArray();
        return new TeamLabExecutionTopology(
            1,
            definition.Name,
            networks,
            networks.Select(network => new TeamLabExecutionInfrastructure(
                ManagedSwitchKey(network.Key),
                $"{network.Name} switch",
                TeamLabInfrastructureKind.ManagedSwitch,
                [],
                network.Key,
                true)).ToArray(),
            assets,
            definition.Connections.Select(connection => new TeamLabExecutionConnection(
                connection.Key,
                connection.FromNetworkKey,
                connection.ToNetworkKey,
                null,
                connection.ViaAssetKey,
                TeamLabConnectionDirection.Bidirectional)).ToArray(),
            BuildLegacyDependencies(assets),
            new TeamLabExecutionObservationPolicy(true, true, TeamLabEndpointObservationMode.Disabled));
    }

    internal static TeamLabExecutionNetwork ToExecution(TeamLabTopologyNetworkModel network) =>
        new(network.Key, network.Name, network.AddressPool.PoolCidr,
            network.AddressPool.RuntimePrefixLength, network.IsEntry, network.OrderIndex);

    internal static TeamLabExecutionAsset ToExecution(TeamLabTopologyAssetModel asset) =>
        new(
            asset.Key,
            asset.Name,
            asset.Kind,
            asset.ImageTemplateId,
            asset.Resources.CpuUnits,
            asset.Resources.MemoryMiB,
            asset.Resources.StorageMiB,
            asset.Interfaces.Select(ToExecution).ToArray(),
            asset.ExposePort,
            asset.HealthCheck?.Kind,
            asset.HealthCheck?.Port,
            asset.OrderIndex,
            asset.EndpointObservation,
            null);

    internal static TeamLabExecutionInterface ToExecution(TeamLabTopologyInterfaceModel iface) =>
        new(iface.Key, iface.NetworkKey, iface.HostOffset, iface.Primary, iface.OrderIndex);

    internal static string ManagedSwitchKey(string networkKey)
    {
        var candidate = $"switch-{networkKey}";
        if (candidate.Length <= 63) return candidate;
        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(networkKey)))[..12];
        return $"switch-{networkKey[..43]}-{suffix}";
    }

    private static IReadOnlyList<TeamLabExecutionDependency> BuildLegacyDependencies(
        IReadOnlyList<TeamLabExecutionAsset> assets)
    {
        var groups = assets.GroupBy(asset => asset.DisplayOrder)
            .OrderBy(group => group.Key)
            .Select(group => group.OrderBy(asset => asset.Key, StringComparer.Ordinal).ToArray())
            .ToArray();
        var dependencies = new List<TeamLabExecutionDependency>();
        for (var index = 1; index < groups.Length; index++)
        {
            foreach (var asset in groups[index])
            foreach (var dependency in groups[index - 1])
            {
                dependencies.Add(new TeamLabExecutionDependency(
                    asset.Key,
                    dependency.Key,
                    dependency.HealthCheckKind is null
                        ? dependency.Kind == TeamLabAssetKind.Vm
                            ? TeamLabDependencyCondition.GuestReady
                            : TeamLabDependencyCondition.NetworkReady
                        : TeamLabDependencyCondition.ServiceReady));
            }
        }
        return dependencies;
    }
}
