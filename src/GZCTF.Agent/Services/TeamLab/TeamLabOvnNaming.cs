using System.Security.Cryptography;
using System.Text;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

internal static class TeamLabOvnNaming
{
    public static string LogicalNetworkName(TeamLabExecutionPlanV2 plan, string key) =>
        $"gzctf-tl-{plan.RuntimePublicId:N}-{plan.Generation}-n-{SafeKey(key)}";

    public static string LogicalPortName(TeamLabExecutionPlanV2 plan, string networkKey, string portKey) =>
        $"gzctf-tl-{plan.RuntimePublicId:N}-{plan.Generation}-p-{SafeKey(networkKey)}-{SafeKey(portKey)}";

    static string SafeKey(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        var prefix = string.IsNullOrEmpty(cleaned) ? "key" : cleaned[..Math.Min(16, cleaned.Length)];
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
        return $"{prefix}-{suffix}";
    }
}
