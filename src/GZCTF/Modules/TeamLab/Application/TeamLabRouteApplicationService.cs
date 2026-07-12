using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRouteApplicationService(
    AppDbContext context,
    ITeamLabNodeExecutor executor)
{
    public async Task ApplyAsync(
        TeamLabRuntime runtime,
        TeamLabTopologyDefinitionModel definition,
        CancellationToken cancellationToken)
    {
        var shards = runtime.Shards.Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.WorkerNodeId).ToArray();
        if (shards.Length == 0)
            throw new TeamLabRuntimeExecutionException("Runtime has no current shard.");
        var nodeIds = shards.Select(item => item.WorkerNodeId).ToArray();
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(item => nodeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var allowedPairs = BuildAllowedPairs(definition);
        var tasks = shards.Select((shard, ordinal) => ApplyShardRoutesAsync(
            runtime, shard, ordinal, shards, nodes, allowedPairs, cancellationToken));
        var results = await Task.WhenAll(tasks);
        var error = results.FirstOrDefault(item => !item.Success);
        if (error is not null)
            throw new TeamLabRuntimeExecutionException(error.Message);
        foreach (var shard in shards)
        {
            shard.RouteVersion = runtime.Generation;
            shard.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<TeamLabNodeResult> ApplyShardRoutesAsync(
        TeamLabRuntime runtime,
        TeamLabRuntimeShard shard,
        int ordinal,
        IReadOnlyList<TeamLabRuntimeShard> shards,
        IReadOnlyDictionary<Guid, WorkerNode> nodes,
        IReadOnlySet<string> allowedPairs,
        CancellationToken cancellationToken)
    {
        if (!nodes.TryGetValue(shard.WorkerNodeId, out var node))
            return TeamLabNodeResult.Failed($"WorkerNode {shard.WorkerNodeId} was not found.");
        var fabricIp = node.TeamLabFabricIp ?? node.TeamLabTunnelIp;
        if (string.IsNullOrWhiteSpace(fabricIp))
            return TeamLabNodeResult.Failed($"WorkerNode '{node.Name}' has no Fabric IP.");
        var networks = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
            .OrderBy(item => item.TopologyKey, StringComparer.Ordinal).ToArray();
        var allNetworks = runtime.Networks.Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.TopologyKey, StringComparer.Ordinal).ToArray();
        var remoteRoutes = new List<TeamLabNodeRouteIntent>();
        foreach (var source in networks)
        foreach (var target in allNetworks.Where(item => item.ShardId != shard.Id))
        {
            if (!allowedPairs.Contains(Pair(source.TopologyKey, target.TopologyKey))) continue;
            var remoteShard = shards.Single(item => item.Id == target.ShardId);
            var remoteNode = nodes[remoteShard.WorkerNodeId];
            var gateway = remoteNode.TeamLabFabricIp ?? remoteNode.TeamLabTunnelIp;
            if (!string.IsNullOrWhiteSpace(gateway))
                remoteRoutes.Add(new TeamLabNodeRouteIntent(target.Cidr, gateway, source.GatewayIp));
        }
        var localRoutes = networks.Select(item => new TeamLabNodeRouteIntent(
            item.Cidr, FabricAddress(runtime.Id, ordinal, 2))).ToArray();
        var policies = networks.SelectMany(source => allNetworks
                .Where(target => target.Id != source.Id)
                .Select(target => new TeamLabNodeForwardPolicy(
                    source.Cidr,
                    target.Cidr,
                    allowedPairs.Contains(Pair(source.TopologyKey, target.TopologyKey)))))
            .ToArray();
        return await executor.ApplyRoutesAsync(shard.WorkerNodeId,
            new TeamLabNodeRouteApplyRequest(
                runtime.Id,
                runtime.Generation,
                runtime.Generation,
                fabricIp,
                RouterName(runtime.Id, shard.Id),
                $"{FabricAddress(runtime.Id, ordinal, 1)}/30",
                $"{FabricAddress(runtime.Id, ordinal, 2)}/30",
                localRoutes,
                remoteRoutes,
                policies), cancellationToken);
    }

    private static IReadOnlySet<string> BuildAllowedPairs(TeamLabTopologyDefinitionModel definition)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in definition.Connections)
        {
            pairs.Add(Pair(connection.FromNetworkKey, connection.ToNetworkKey));
            pairs.Add(Pair(connection.ToNetworkKey, connection.FromNetworkKey));
        }
        return pairs;
    }

    private static string Pair(string source, string destination) => $"{source}\n{destination}";

    internal static string RouterName(int runtimeId, int shardId) => LinuxName($"tlr{runtimeId}-{shardId}");
    internal static string WireGuardName(int runtimeId) => LinuxName($"tlwg{runtimeId}");

    private static string FabricAddress(int runtimeId, int shardOrdinal, int hostOffset)
    {
        const int blocksPerRuntime = 32;
        const int totalBlocks = 16384;
        var runtimeBucket = Math.Abs(runtimeId % (totalBlocks / blocksPerRuntime));
        var shardBucket = Math.Abs(shardOrdinal % blocksPerRuntime);
        var normalized = runtimeBucket * blocksPerRuntime + shardBucket;
        return $"169.254.{normalized / 64}.{(normalized % 64) * 4 + hostOffset}";
    }

    internal static string LinuxName(string value) => value.Length <= 15 ? value : value[..15];
}

public sealed class TeamLabRuntimeExecutionException(string message) : Exception(message);
