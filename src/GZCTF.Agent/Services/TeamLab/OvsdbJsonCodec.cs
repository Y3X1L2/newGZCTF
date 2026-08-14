using System.Text.Json.Nodes;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

static class OvsdbJsonCodec
{
    public static JsonArray Map(params (string Key, string Value)[] pairs)
    {
        var entries = new JsonArray();
        foreach (var (key, value) in pairs)
            entries.Add(new JsonArray { key, value });
        return new JsonArray { "map", entries };
    }

    public static string? GetMapValue(JsonNode? value, string key)
    {
        if (value is not JsonArray map || map.Count != 2 ||
            !string.Equals(map[0]?.GetValue<string>(), "map", StringComparison.Ordinal) ||
            map[1] is not JsonArray entries)
            return null;
        foreach (var entry in entries.OfType<JsonArray>())
            if (entry.Count == 2 &&
                string.Equals(entry[0]?.GetValue<string>(), key, StringComparison.Ordinal))
                return entry[1]?.GetValue<string>();
        return null;
    }

    public static JsonArray OwnedWhere(TeamLabExecutionPlanV2 plan) => new()
    {
        new JsonArray
        {
            "external_ids",
            "includes",
            Map(
                ("gzctf-runtime", plan.RuntimePublicId.ToString("D")),
                ("gzctf-generation", plan.Generation.ToString()),
                ("gzctf-plan-digest", plan.PlanDigest))
        }
    };
}
