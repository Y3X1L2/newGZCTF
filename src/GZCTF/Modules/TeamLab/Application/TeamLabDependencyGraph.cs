using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Application;

public enum TeamLabDeploymentNodeKind : byte
{
    Create = 0,
    GuestReady = 1,
    Bootstrap = 2,
    Health = 3
}

public sealed record TeamLabDeploymentNode(string AssetKey, TeamLabDeploymentNodeKind Kind)
{
    public string Key => $"{AssetKey}:{Kind.ToString().ToLowerInvariant()}";
}

public sealed class TeamLabDependencyGraph
{
    private readonly IReadOnlyDictionary<string, TeamLabDeploymentNode> _nodes;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _prerequisites;

    private TeamLabDependencyGraph(
        IReadOnlyDictionary<string, TeamLabDeploymentNode> nodes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> prerequisites)
    {
        _nodes = nodes;
        _prerequisites = prerequisites;
    }

    public int Count => _nodes.Count;

    public static TeamLabDependencyGraph Compile(TeamLabExecutionTopology topology)
    {
        var nodes = new Dictionary<string, TeamLabDeploymentNode>(StringComparer.Ordinal);
        var prerequisites = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var asset in topology.Assets)
        {
            Add(new TeamLabDeploymentNode(asset.Key, TeamLabDeploymentNodeKind.Create));
            if (asset.Kind == TeamLabAssetKind.Vm)
                Add(new TeamLabDeploymentNode(asset.Key, TeamLabDeploymentNodeKind.GuestReady));
            Add(new TeamLabDeploymentNode(asset.Key, TeamLabDeploymentNodeKind.Bootstrap));
            Add(new TeamLabDeploymentNode(asset.Key, TeamLabDeploymentNodeKind.Health));
            Require(Key(asset.Key, TeamLabDeploymentNodeKind.Bootstrap),
                Key(asset.Key, asset.Kind == TeamLabAssetKind.Vm
                    ? TeamLabDeploymentNodeKind.GuestReady
                    : TeamLabDeploymentNodeKind.Create));
            if (asset.Kind == TeamLabAssetKind.Vm)
                Require(Key(asset.Key, TeamLabDeploymentNodeKind.GuestReady),
                    Key(asset.Key, TeamLabDeploymentNodeKind.Create));
            Require(Key(asset.Key, TeamLabDeploymentNodeKind.Health),
                Key(asset.Key, TeamLabDeploymentNodeKind.Bootstrap));
        }

        foreach (var dependency in topology.Dependencies)
        {
            if (dependency.Condition == TeamLabDependencyCondition.NetworkReady)
                continue;
            var dependencyAsset = topology.Assets.Single(asset => asset.Key == dependency.DependsOnKey);
            var requiredKind = dependency.Condition switch
            {
                TeamLabDependencyCondition.GuestReady => dependencyAsset.Kind == TeamLabAssetKind.Vm
                    ? TeamLabDeploymentNodeKind.GuestReady
                    : TeamLabDeploymentNodeKind.Create,
                TeamLabDependencyCondition.BootstrapCompleted => TeamLabDeploymentNodeKind.Bootstrap,
                TeamLabDependencyCondition.ServiceReady => TeamLabDeploymentNodeKind.Health,
                _ => throw new ArgumentOutOfRangeException(nameof(dependency.Condition))
            };
            Require(Key(dependency.AssetKey, TeamLabDeploymentNodeKind.Create),
                Key(dependency.DependsOnKey, requiredKind));
        }

        return new TeamLabDependencyGraph(
            nodes,
            prerequisites.ToDictionary(
                item => item.Key,
                item => (IReadOnlySet<string>)item.Value,
                StringComparer.Ordinal));

        void Add(TeamLabDeploymentNode node)
        {
            nodes.Add(node.Key, node);
            prerequisites[node.Key] = new HashSet<string>(StringComparer.Ordinal);
        }

        void Require(string node, string prerequisite)
        {
            if (!prerequisites.TryGetValue(node, out var values) || !nodes.ContainsKey(prerequisite))
                throw new InvalidOperationException("TeamLab dependency graph references an unknown deployment node.");
            values.Add(prerequisite);
        }
    }

    public bool TryTakeReadyBatch(
        IReadOnlySet<string> completed,
        IReadOnlySet<string> scheduled,
        out IReadOnlyList<TeamLabDeploymentNode> batch)
    {
        batch = _nodes.Values
            .Where(node => !completed.Contains(node.Key) && !scheduled.Contains(node.Key) &&
                           _prerequisites[node.Key].All(completed.Contains))
            .OrderBy(node => node.Kind)
            .ThenBy(node => node.AssetKey, StringComparer.Ordinal)
            .ToArray();
        return batch.Count > 0;
    }

    public IReadOnlyList<string> DescribeBlocked(
        IReadOnlySet<string> completed,
        IReadOnlySet<string> scheduled) => _nodes.Values
        .Where(node => !completed.Contains(node.Key) && !scheduled.Contains(node.Key))
        .OrderBy(node => node.Key, StringComparer.Ordinal)
        .Select(node => $"{node.Key} waits for [{string.Join(",", _prerequisites[node.Key].Where(key => !completed.Contains(key)).Order(StringComparer.Ordinal))}]")
        .ToArray();

    public static HashSet<string> RestoreCompletedNodes(IEnumerable<TeamLabRuntimeAsset> assets)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            if (!string.IsNullOrWhiteSpace(asset.RuntimeResourceId) &&
                asset.ExecutionStage < TeamLabAssetExecutionStage.Failed)
                completed.Add(Key(asset.TopologyKey, TeamLabDeploymentNodeKind.Create));
            if (asset.Kind == TeamLabResourceKind.Vm &&
                asset.ExecutionStage is >= TeamLabAssetExecutionStage.GuestReady and < TeamLabAssetExecutionStage.Failed)
                completed.Add(Key(asset.TopologyKey, TeamLabDeploymentNodeKind.GuestReady));
            if (asset.ExecutionStage is >= TeamLabAssetExecutionStage.BootstrapCompleted and < TeamLabAssetExecutionStage.Failed)
                completed.Add(Key(asset.TopologyKey, TeamLabDeploymentNodeKind.Bootstrap));
            if (asset.ExecutionStage == TeamLabAssetExecutionStage.ServiceReady)
                completed.Add(Key(asset.TopologyKey, TeamLabDeploymentNodeKind.Health));
        }

        return completed;
    }

    public static string Key(string assetKey, TeamLabDeploymentNodeKind kind) =>
        $"{assetKey}:{kind.ToString().ToLowerInvariant()}";
}
