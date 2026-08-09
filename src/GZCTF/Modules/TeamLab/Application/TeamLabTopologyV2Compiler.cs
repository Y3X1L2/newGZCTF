using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public static class TeamLabTopologyV2Compiler
{
    public static TeamLabExecutionTopology Compile(TeamLabTopologyDefinitionModel definition)
    {
        var networks = definition.Networks.Select(TeamLabTopologyV1Normalizer.ToExecution).ToArray();
        var infrastructure = (definition.Infrastructure ?? [])
            .Select(item => new TeamLabExecutionInfrastructure(
                item.Key,
                item.Name,
                item.Kind,
                item.Interfaces.Select(TeamLabTopologyV1Normalizer.ToExecution).ToArray(),
                item.NetworkKey,
                false))
            .ToList();
        var switchedNetworks = infrastructure
            .Where(item => item.Kind == TeamLabInfrastructureKind.ManagedSwitch && item.NetworkKey is not null)
            .Select(item => item.NetworkKey!)
            .ToHashSet(StringComparer.Ordinal);
        infrastructure.AddRange(networks
            .Where(network => !switchedNetworks.Contains(network.Key))
            .Select(network => new TeamLabExecutionInfrastructure(
                TeamLabTopologyV1Normalizer.ManagedSwitchKey(network.Key),
                $"{network.Name} switch",
                TeamLabInfrastructureKind.ManagedSwitch,
                [],
                network.Key,
                true)));
        var observation = definition.Observation ?? new TeamLabObservationPolicyModel();
        return new TeamLabExecutionTopology(
            2,
            definition.Name,
            networks,
            infrastructure.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray(),
            definition.Assets.Select(TeamLabTopologyV1Normalizer.ToExecution).ToArray(),
            definition.Connections.Select(connection => new TeamLabExecutionConnection(
                connection.Key,
                connection.FromNetworkKey,
                connection.ToNetworkKey,
                connection.ViaNodeKey,
                connection.ViaAssetKey,
                connection.Direction ?? TeamLabConnectionDirection.Bidirectional)).ToArray(),
            (definition.Dependencies ?? []).Select(dependency => new TeamLabExecutionDependency(
                dependency.AssetKey, dependency.DependsOnKey, dependency.Condition)).ToArray(),
            new TeamLabExecutionObservationPolicy(
                observation.FlowMetadataEnabled,
                observation.OnDemandPcapEnabled,
                observation.EndpointObservation));
    }
}
