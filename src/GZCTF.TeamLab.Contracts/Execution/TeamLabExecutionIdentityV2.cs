using System.Security.Cryptography;
using System.Text;

namespace GZCTF.TeamLab.Contracts.Execution;

public static class TeamLabExecutionIdentityV2
{
    public static string VmDomainName(Guid runtimePublicId, int generation, string shardKey, string assetKey)
    {
        var builder = new StringBuilder(assetKey.Length);
        foreach (var character in assetKey)
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
                builder.Append(character);
        var safeAsset = builder.Length == 0 ? "asset" : builder.ToString()[..Math.Min(48, builder.Length)];
        var shardHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(shardKey)))[..8].ToLowerInvariant();
        return $"gzctf-tl-{runtimePublicId:N}-{generation}-{shardHash}-{safeAsset}";
    }

    public static string VmDomainName(Guid runtimePublicId, int generation, string assetKey) =>
        VmDomainName(runtimePublicId, generation, "legacy", assetKey);

    /// <summary>
    /// Returns the stable host-side veth name used for a Docker workload interface.
    /// The device is created by the Agent and must match the execution-plan observation token.
    /// </summary>
    public static string WorkloadHostInterface(Guid runtimePublicId, int generation, string assetKey, string networkKey) =>
        StableDeviceName("tlh", $"{runtimePublicId:D}:{generation}:{assetKey}:{networkKey}");

    /// <summary>
    /// Returns the stable libvirt TAP device name used for a VM workload interface.
    /// </summary>
    public static string VmTapName(Guid runtimePublicId, int generation, string assetKey, string networkKey) =>
        StableDeviceName("tlv", $"{runtimePublicId:D}:{generation}:{assetKey}:{networkKey}");

    static string StableDeviceName(string prefix, string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        return $"{prefix}{hash[..Math.Min(12, hash.Length)]}"[..15];
    }
}
