namespace GZCTF.Modules.TeamLab.Application;

using System.Security.Cryptography;
using System.Text;

public static class TeamLabResourceNameFactory
{
    public static string Bridge(int runtimeId, string networkKey) => LinuxName($"tl{runtimeId}-{networkKey}");
    public static string RouterNamespace(int runtimeId, int shardId) => LinuxName($"tlr{runtimeId}-{shardId}");
    public static string WireGuardInterface(int runtimeId) => LinuxName($"tlwg{runtimeId}");
    public static string DhcpDnsService(int runtimeId, string networkKey) => LinuxName($"tld{runtimeId}-{networkKey}");
    public static string FabricHostInterface(int runtimeId) => LinuxName($"tlrf{runtimeId}");
    public static string FabricNamespaceInterface(int runtimeId) => LinuxName($"tlrf{runtimeId}n");
    public static string WorkloadHostInterface(int runtimeId, string assetKey, string interfaceKey)
    {
        var stableId = BitConverter.ToInt32(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{assetKey}:{interfaceKey}")), 0) & int.MaxValue;
        return LinuxName($"tl{runtimeId}v{stableId:x}");
    }
    public static string LinuxName(string value)
    {
        if (value.Length <= 15) return value;
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..6];
        return $"{value[..8]}-{digest}";
    }
}
