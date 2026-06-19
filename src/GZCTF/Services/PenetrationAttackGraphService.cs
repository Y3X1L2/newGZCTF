using System.Collections.Concurrent;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Services;

public sealed class PenetrationAttackGraphService(IMemoryCache memoryCache)
{
    static readonly ConcurrentDictionary<string, Lazy<PenetrationAttackGraphModel>> CacheBuilders =
        new(StringComparer.Ordinal);

    static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        SlidingExpiration = TimeSpan.FromSeconds(45)
    };

    public PenetrationAttackGraphModel GetOrBuild(
        PenetrationConfig config,
        PenetrationTeamEnvironment environment,
        ISet<string> solvedScoreItemKeys)
    {
        var cacheKey = BuildCacheKey(environment, solvedScoreItemKeys);
        if (memoryCache.TryGetValue(cacheKey, out PenetrationAttackGraphModel? cached) && cached is not null)
            return cached;

        var builder = CacheBuilders.GetOrAdd(cacheKey, _ => new Lazy<PenetrationAttackGraphModel>(() =>
        {
            if (memoryCache.TryGetValue(cacheKey, out PenetrationAttackGraphModel? current) && current is not null)
                return current;

            var graph = Build(config, environment, solvedScoreItemKeys);
            memoryCache.Set(cacheKey, graph, CacheOptions);
            return graph;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return builder.Value;
        }
        finally
        {
            CacheBuilders.TryRemove(cacheKey, out _);
        }
    }

    public PenetrationAttackGraphModel Build(
        PenetrationConfig config,
        PenetrationTeamEnvironment environment,
        ISet<string> solvedScoreItemKeys)
    {
        var nodes = config.Nodes
            .OrderBy(n => n.OrderIndex)
            .ToArray();
        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        var routeEdges = config.Edges
            .Where(e => e.PolicyAction == PenetrationPolicyAction.Allow &&
                        e.IsRouteHint &&
                        e.SourceNodeId > 0 &&
                        e.TargetNodeId > 0)
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.Id)
            .ToArray();
        var outgoing = routeEdges
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var depthByNodeId = BuildDepths(nodes, outgoing);
        var accessibleNodeIds = new HashSet<int>();
        foreach (var node in nodes.Where(n => n.IsEntry))
            accessibleNodeIds.Add(node.Id);

        var completedNodeIds = new HashSet<int>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in nodes.Where(n => accessibleNodeIds.Contains(n.Id)))
            {
                if (!completedNodeIds.Contains(node.Id) && IsNodeCompleted(node, solvedScoreItemKeys))
                {
                    completedNodeIds.Add(node.Id);
                    changed = true;
                }

                if (!completedNodeIds.Contains(node.Id) || !outgoing.TryGetValue(node.Id, out var edges))
                    continue;

                foreach (var edge in edges)
                    if (accessibleNodeIds.Add(edge.TargetNodeId))
                        changed = true;
            }
        }

        var revealedNodeIds = new HashSet<int>(accessibleNodeIds);
        foreach (var edge in routeEdges)
        {
            if (completedNodeIds.Contains(edge.SourceNodeId) ||
                completedNodeIds.Contains(edge.TargetNodeId))
            {
                revealedNodeIds.Add(edge.SourceNodeId);
                revealedNodeIds.Add(edge.TargetNodeId);
            }
        }

        foreach (var id in completedNodeIds)
            revealedNodeIds.Add(id);

        var runtimeByKey = environment.RuntimeNodes
            .Where(r => !string.IsNullOrWhiteSpace(r.TopologyNodeKey))
            .ToDictionary(r => r.TopologyNodeKey, StringComparer.Ordinal);

        var graphNodes = nodes.Select(node =>
        {
            var status = ResolveStatus(node.Id, revealedNodeIds, accessibleNodeIds, completedNodeIds);
            runtimeByKey.TryGetValue(node.TopologyKey, out var runtime);
            var safe = status != PenetrationFogState.Hidden;
            return new PenetrationAttackNodeModel
            {
                Id = safe ? node.Id : 0,
                TopologyKey = safe ? node.TopologyKey : $"fog-depth-{depthByNodeId.GetValueOrDefault(node.Id, -1)}-{node.OrderIndex}",
                DisplayName = safe ? BuildPlayerDisplayName(node) : "未知区域",
                Description = safe ? node.PlayerDescription : null,
                Depth = depthByNodeId.GetValueOrDefault(node.Id, node.IsEntry ? 0 : -1),
                Status = status,
                ScoreSummary = safe ? BuildScoreSummary(node, solvedScoreItemKeys) : new PenetrationAttackScoreSummaryModel(),
                PositionX = safe ? node.PositionX : Math.Max(0, depthByNodeId.GetValueOrDefault(node.Id, -1)) * 260,
                PositionY = safe ? node.PositionY : node.OrderIndex * 96,
                IsEntry = node.IsEntry,
                IsCheckpointCompleted = completedNodeIds.Contains(node.Id),
                RuntimeStatus = safe ? runtime?.Status ?? PenetrationRuntimeStatus.Pending : PenetrationRuntimeStatus.Pending
            };
        }).ToList();

        var visibleNodeIds = graphNodes
            .Where(n => n.Status != PenetrationFogState.Hidden && n.Id > 0)
            .Select(n => n.Id)
            .ToHashSet();

        var graphEdges = routeEdges
            .Where(e => nodeIds.Contains(e.SourceNodeId) && nodeIds.Contains(e.TargetNodeId))
            .Where(e => visibleNodeIds.Contains(e.SourceNodeId) && visibleNodeIds.Contains(e.TargetNodeId))
            .Select(e =>
            {
                var source = nodes.First(n => n.Id == e.SourceNodeId);
                var target = nodes.First(n => n.Id == e.TargetNodeId);
                var edgeStatus = completedNodeIds.Contains(e.SourceNodeId) && accessibleNodeIds.Contains(e.TargetNodeId)
                    ? PenetrationFogState.Accessible
                    : PenetrationFogState.Revealed;
                return new PenetrationAttackEdgeModel
                {
                    Id = e.Id,
                    SourceNodeKey = source.TopologyKey,
                    TargetNodeKey = target.TopologyKey,
                    Status = edgeStatus,
                    Label = string.IsNullOrWhiteSpace(e.Label) ? "攻击路径" : e.Label!
                };
            }).ToList();

        return new PenetrationAttackGraphModel
        {
            GameId = environment.GameId,
            TeamId = environment.TeamId,
            PublishedVersion = environment.PublishedVersion,
            TotalNodeCount = nodes.Length,
            VisibleNodeCount = graphNodes.Count(n => n.Status != PenetrationFogState.Hidden),
            CompletedNodeCount = completedNodeIds.Count,
            TotalScoreItemCount = nodes.Sum(n => n.ScoreItems.Count(i => i.IsVisible)),
            SolvedScoreItemCount = nodes.SelectMany(n => n.ScoreItems)
                .Count(i => i.IsVisible && solvedScoreItemKeys.Contains(i.TopologyKey)),
            Nodes = graphNodes,
            Edges = graphEdges
        };
    }

    static Dictionary<int, int> BuildDepths(IEnumerable<PenetrationNode> nodes,
        IReadOnlyDictionary<int, PenetrationEdge[]> outgoing)
    {
        var depthByNodeId = new Dictionary<int, int>();
        var queue = new Queue<int>();

        foreach (var entry in nodes.Where(n => n.IsEntry).OrderBy(n => n.OrderIndex))
        {
            if (depthByNodeId.TryAdd(entry.Id, 0))
                queue.Enqueue(entry.Id);
        }

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var depth = depthByNodeId[currentId];
            if (!outgoing.TryGetValue(currentId, out var edges))
                continue;

            foreach (var edge in edges)
            {
                var nextDepth = depth + 1;
                if (depthByNodeId.TryGetValue(edge.TargetNodeId, out var saved) && saved <= nextDepth)
                    continue;

                depthByNodeId[edge.TargetNodeId] = nextDepth;
                queue.Enqueue(edge.TargetNodeId);
            }
        }

        return depthByNodeId;
    }

    static bool IsNodeCompleted(PenetrationNode node, ISet<string> solvedScoreItemKeys)
    {
        var visibleItems = node.ScoreItems.Where(i => i.IsVisible).ToArray();
        if (visibleItems.Length == 0)
            return true;

        var checkpoints = visibleItems.Where(i => i.IsCheckpoint).ToArray();
        var blockers = checkpoints.Length > 0 ? checkpoints : visibleItems;
        return blockers.All(i => solvedScoreItemKeys.Contains(i.TopologyKey));
    }

    static string BuildCacheKey(PenetrationTeamEnvironment environment, ISet<string> solvedScoreItemKeys)
    {
        var solvedFingerprint = string.Join(',', solvedScoreItemKeys.Order(StringComparer.Ordinal));
        var statusStamp = environment.UpdatedAt?.ToUnixTimeMilliseconds() ?? environment.CreatedAt.ToUnixTimeMilliseconds();
        return $"_PentestAttackGraph_{environment.GameId}_{environment.TeamId}_{environment.PublishedVersion}_{environment.Status}_{statusStamp}_{solvedFingerprint}";
    }

    static PenetrationFogState ResolveStatus(int nodeId, ISet<int> revealedNodeIds, ISet<int> accessibleNodeIds,
        ISet<int> completedNodeIds)
    {
        if (completedNodeIds.Contains(nodeId))
            return PenetrationFogState.Completed;
        if (accessibleNodeIds.Contains(nodeId))
            return PenetrationFogState.Accessible;
        return revealedNodeIds.Contains(nodeId) ? PenetrationFogState.Revealed : PenetrationFogState.Hidden;
    }

    static string BuildPlayerDisplayName(PenetrationNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.PlayerAlias))
            return node.PlayerAlias!;

        return node.IsEntry ? "入口目标" : $"目标模块 {node.OrderIndex + 1}";
    }

    static PenetrationAttackScoreSummaryModel BuildScoreSummary(PenetrationNode node, ISet<string> solvedScoreItemKeys)
    {
        var visibleItems = node.ScoreItems.Where(i => i.IsVisible).ToArray();
        var checkpoints = visibleItems.Where(i => i.IsCheckpoint).ToArray();
        var solved = visibleItems.Where(i => solvedScoreItemKeys.Contains(i.TopologyKey)).ToArray();

        return new PenetrationAttackScoreSummaryModel
        {
            Total = visibleItems.Length,
            Solved = solved.Length,
            CheckpointTotal = checkpoints.Length,
            CheckpointSolved = checkpoints.Count(i => solvedScoreItemKeys.Contains(i.TopologyKey)),
            TotalScore = visibleItems.Sum(i => i.Score),
            SolvedScore = solved.Sum(i => i.Score)
        };
    }
}
