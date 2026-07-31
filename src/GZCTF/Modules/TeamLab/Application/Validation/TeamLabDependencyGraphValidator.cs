using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application.Validation;

internal sealed class TeamLabDependencyGraphValidator
{
    public void Validate(
        TeamLabTopologyDefinitionModel definition,
        ICollection<TeamLabValidationIssueModel> issues)
    {
        var assets = definition.Assets.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var dependencies = definition.Dependencies ?? [];
        var unique = new HashSet<(string AssetKey, string DependsOnKey, Domain.TeamLabDependencyCondition Condition)>();
        var graph = assets.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (dependency, index) in dependencies.Select((value, index) => (value, index)))
        {
            var path = $"dependencies[{index}]";
            if (!assets.Contains(dependency.AssetKey) || !assets.Contains(dependency.DependsOnKey))
            {
                issues.Add(new TeamLabValidationIssueModel(
                    "dependency_asset_missing",
                    path,
                    "Both dependency assets must exist."));
                continue;
            }
            if (dependency.AssetKey == dependency.DependsOnKey)
            {
                issues.Add(new TeamLabValidationIssueModel(
                    "dependency_self_reference",
                    path,
                    "An asset cannot depend on itself."));
                continue;
            }
            if (!unique.Add((dependency.AssetKey, dependency.DependsOnKey, dependency.Condition)))
                issues.Add(new TeamLabValidationIssueModel(
                    "dependency_duplicate",
                    path,
                    "The dependency occurs more than once."));
            graph[dependency.DependsOnKey].Add(dependency.AssetKey);
        }

        var state = assets.ToDictionary(key => key, _ => VisitState.Unvisited, StringComparer.Ordinal);
        foreach (var asset in assets.OrderBy(item => item, StringComparer.Ordinal))
        {
            if (HasCycle(asset, graph, state))
            {
                issues.Add(new TeamLabValidationIssueModel(
                    "dependency_cycle",
                    "dependencies",
                    "The asset dependency graph contains a cycle."));
                return;
            }
        }
    }

    private static bool HasCycle(
        string key,
        IReadOnlyDictionary<string, List<string>> graph,
        IDictionary<string, VisitState> state)
    {
        if (state[key] == VisitState.Visiting) return true;
        if (state[key] == VisitState.Visited) return false;
        state[key] = VisitState.Visiting;
        foreach (var next in graph[key])
        {
            if (HasCycle(next, graph, state)) return true;
        }
        state[key] = VisitState.Visited;
        return false;
    }

    private enum VisitState : byte
    {
        Unvisited,
        Visiting,
        Visited
    }
}
