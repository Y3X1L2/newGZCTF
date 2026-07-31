using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application.Validation;

internal static class TeamLabReachabilityCompiler
{
    public static IReadOnlySet<string> Compile(TeamLabExecutionTopology topology)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in topology.Connections)
        {
            pairs.Add(Pair(connection.FromNetworkKey, connection.ToNetworkKey));
            if (connection.Direction == TeamLabConnectionDirection.Bidirectional)
                pairs.Add(Pair(connection.ToNetworkKey, connection.FromNetworkKey));
        }
        return pairs;
    }

    public static IReadOnlySet<string> CompileRouting(TeamLabExecutionTopology topology)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in topology.Connections)
        {
            pairs.Add(Pair(connection.FromNetworkKey, connection.ToNetworkKey));
            pairs.Add(Pair(connection.ToNetworkKey, connection.FromNetworkKey));
        }
        return pairs;
    }

    public static string Pair(string source, string target) => $"{source}\n{target}";
}
