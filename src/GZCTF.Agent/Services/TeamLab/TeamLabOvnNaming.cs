using System.Security.Cryptography;
using System.Text;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

internal static class TeamLabOvnNaming
{
    public static string OvsdbId(string kind, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{value}"));
        return $"gzctf_{kind.Replace('-', '_')}_{new Guid(bytes[..16]):N}";
    }

    public static string LogicalNetworkName(TeamLabExecutionPlanV2 plan, string key) =>
        $"gzctf-tl-{plan.RuntimePublicId:N}-{plan.Generation}-n-{SafeKey(key)}";

    // libvirt requires OVS interface ids to be UUIDs, and OVN binds a port by
    // matching iface-id against Logical_Switch_Port.name, so the same
    // deterministic UUID is the identity shared by OVN, OVS and libvirt.
    public static string LogicalPortId(Guid runtimePublicId, int generation, string networkKey, string portKey) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"gzctf-tl-port:{runtimePublicId:D}:{generation}:{networkKey}:{portKey}"))[..16]).ToString("D");

    public static string LogicalPortId(TeamLabExecutionPlanV2 plan, string networkKey, string portKey) =>
        LogicalPortId(plan.RuntimePublicId, plan.Generation, networkKey, portKey);

    static string SafeKey(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        var prefix = string.IsNullOrEmpty(cleaned) ? "key" : cleaned[..Math.Min(16, cleaned.Length)];
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
        return $"{prefix}-{suffix}";
    }
}
