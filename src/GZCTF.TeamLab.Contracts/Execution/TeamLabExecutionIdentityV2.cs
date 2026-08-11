using System.Text;

namespace GZCTF.TeamLab.Contracts.Execution;

public static class TeamLabExecutionIdentityV2
{
    public static string VmDomainName(Guid runtimePublicId, int generation, string assetKey)
    {
        var builder = new StringBuilder(assetKey.Length);
        foreach (var character in assetKey)
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
                builder.Append(character);
        var safeAsset = builder.Length == 0 ? "asset" : builder.ToString()[..Math.Min(48, builder.Length)];
        return $"gzctf-tl-{runtimePublicId:N}-{generation}-{safeAsset}";
    }
}
