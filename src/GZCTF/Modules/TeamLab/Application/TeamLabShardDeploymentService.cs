using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabShardDeploymentService(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    TeamLabRouteApplicationService routes)
{
    public async Task DeployAsync(
        TeamLabRuntime runtime,
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, TeamLabRuntimeOverlayModel> overlays,
        CancellationToken cancellationToken)
    {
        var currentShards = runtime.Shards.Where(item => item.Generation == runtime.Generation).ToArray();
        var networkApply = await Task.WhenAll(currentShards.Select(shard =>
            executor.ApplyShardAsync(shard.WorkerNodeId, BuildShardRequest(runtime, shard), cancellationToken)));
        var networkError = networkApply.FirstOrDefault(item => !item.Success);
        if (networkError is not null) throw new TeamLabRuntimeExecutionException(networkError.Message);

        await routes.ApplyAsync(runtime, definition, cancellationToken);
        var topologyAssets = definition.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var allowedRoutes = BuildAllowedRoutes(runtime, definition);
        foreach (var orderGroup in definition.Assets.GroupBy(item => item.OrderIndex).OrderBy(item => item.Key))
        {
            var runtimeAssets = runtime.Assets.Where(item => item.Generation == runtime.Generation &&
                                                              orderGroup.Any(source => source.Key == item.TopologyKey))
                .OrderBy(item => item.TopologyKey, StringComparer.Ordinal).ToArray();
            var results = await Task.WhenAll(runtimeAssets.Select(asset => CreateAssetAsync(
                runtime, asset, topologyAssets[asset.TopologyKey], overlays.GetValueOrDefault(asset.TopologyKey),
                allowedRoutes, cancellationToken)));
            foreach (var result in results)
            {
                result.Asset.Status = result.Result.Success ? TeamLabRuntimeStatus.Running : TeamLabRuntimeStatus.Failed;
                result.Asset.RuntimeResourceId = result.Result.RuntimeResourceId;
                result.Asset.LastError = result.Result.Success ? null : Trim(result.Result.Message);
            }
            await context.SaveChangesAsync(cancellationToken);
            var failure = results.FirstOrDefault(item => !item.Result.Success);
            if (failure is not null) throw new TeamLabRuntimeExecutionException(failure.Result.Message);
        }

        runtime.Status = TeamLabRuntimeStatus.Probing;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => runtime.Assets.Select(asset => asset.SourceTemplateId).Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var asset in runtime.Assets.Where(item => item.Generation == runtime.Generation &&
                                                            item.Kind is TeamLabResourceKind.Docker or TeamLabResourceKind.Vm))
        {
            if (asset.Kind == TeamLabResourceKind.Vm && asset.SourceTemplateId is { } templateId &&
                templates.GetValueOrDefault(templateId)?.OSType == OSType.Windows)
                continue;
            var shard = runtime.Shards.Single(item => item.Id == asset.ShardId);
            var probe = await executor.ProbeAsync(shard.WorkerNodeId,
                new TeamLabNodeProbeRequest(runtime.Id, TeamLabRouteApplicationService.RouterName(runtime.Id, shard.Id), asset.IpAddress!),
                cancellationToken);
            if (!probe.Success) throw new TeamLabRuntimeExecutionException(probe.Message);
        }
    }

    private TeamLabNodeShardApplyRequest BuildShardRequest(TeamLabRuntime runtime, TeamLabRuntimeShard shard)
    {
        var networks = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
            .OrderBy(item => item.TopologyKey, StringComparer.Ordinal).ToArray();
        var records = networks.ToDictionary(
            network => network.TopologyKey,
            network => (IReadOnlyList<TeamLabNodeDnsRecord>)runtime.Assets
                .Where(asset => asset.Generation == runtime.Generation && asset.ShardId == shard.Id)
                .SelectMany(asset => ParseInterfaces(asset).Where(iface => iface.NetworkKey == network.TopologyKey)
                    .Select(iface => new TeamLabNodeDnsRecord(asset.TopologyKey, iface.IpAddress, iface.MacAddress)))
                .ToArray(),
            StringComparer.Ordinal);
        return new TeamLabNodeShardApplyRequest(
            runtime.Id,
            runtime.Generation,
            TeamLabRouteApplicationService.RouterName(runtime.Id, shard.Id),
            networks.Select(item => new TeamLabNodeNetworkIntent(
                item.TopologyKey, item.Name, item.Cidr, item.GatewayIp, item.BridgeName)).ToArray(),
            records);
    }

    private async Task<AssetCreation> CreateAssetAsync(
        TeamLabRuntime runtime,
        TeamLabRuntimeAsset asset,
        TeamLabTopologyAssetModel topologyAsset,
        TeamLabRuntimeOverlayModel? overlay,
        IReadOnlyDictionary<string, IReadOnlyList<string>> allowedRoutes,
        CancellationToken cancellationToken)
    {
        var shard = runtime.Shards.Single(item => item.Id == asset.ShardId);
        var interfaces = ParseInterfaces(asset).Select(iface =>
        {
            var network = runtime.Networks.Single(item => item.Generation == runtime.Generation && item.TopologyKey == iface.NetworkKey);
            return new TeamLabNodeInterfaceIntent(
                iface.Key, iface.NetworkKey, network.BridgeName, iface.IpAddress, iface.PrefixLength,
                iface.MacAddress, iface.Primary,
                allowedRoutes.GetValueOrDefault(iface.NetworkKey) ?? [],
                runtime.Networks.Where(item => item.Generation == runtime.Generation).Select(item => item.GatewayIp).ToArray());
        }).ToArray();
        var environment = (topologyAsset.Environment ?? new Dictionary<string, string>())
            .Concat(overlay?.Environment ?? new Dictionary<string, string>())
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var secrets = overlay?.Secrets ?? new Dictionary<string, string>();
        var result = await executor.CreateAssetAsync(shard.WorkerNodeId,
            new TeamLabNodeAssetCreateRequest(
                runtime.Id, runtime.Generation, asset.TopologyKey, asset.Name, topologyAsset.Kind,
                topologyAsset.ImageTemplateId, topologyAsset.Resources.CpuUnits, topologyAsset.Resources.MemoryMiB,
                topologyAsset.Resources.StorageMiB, topologyAsset.ExposePort, topologyAsset.RoutingEnabled,
                environment, secrets, interfaces), cancellationToken);
        return new AssetCreation(asset, result);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildAllowedRoutes(
        TeamLabRuntime runtime,
        TeamLabTopologyDefinitionModel definition)
    {
        var networkByKey = runtime.Networks.Where(item => item.Generation == runtime.Generation)
            .ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
        var targets = networkByKey.Keys.ToDictionary(key => key, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var connection in definition.Connections)
        {
            if (!networkByKey.ContainsKey(connection.FromNetworkKey) || !networkByKey.ContainsKey(connection.ToNetworkKey)) continue;
            targets[connection.FromNetworkKey].Add(networkByKey[connection.ToNetworkKey].Cidr);
            targets[connection.ToNetworkKey].Add(networkByKey[connection.FromNetworkKey].Cidr);
        }
        foreach (var asset in definition.Assets.Where(item => item.RoutingEnabled))
        {
            var attached = asset.Interfaces.Select(item => item.NetworkKey).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var source in attached)
            foreach (var target in attached.Where(item => item != source))
                if (targets.ContainsKey(source) && networkByKey.ContainsKey(target)) targets[source].Add(networkByKey[target].Cidr);
        }
        return targets.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)item.Value.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    private static RuntimeInterfaceIntent[] ParseInterfaces(TeamLabRuntimeAsset asset) =>
        JsonSerializer.Deserialize<RuntimeInterfaceIntent[]>(asset.InterfaceSummaryJson) ?? [];

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];

    private sealed record AssetCreation(TeamLabRuntimeAsset Asset, TeamLabNodeAssetCreateResult Result);
    private sealed record RuntimeInterfaceIntent(
        string Key,
        string NetworkKey,
        string IpAddress,
        int PrefixLength,
        string MacAddress,
        bool Primary);
}
