using System.Security.Cryptography;
using System.Text;

namespace GZCTF.Utils;

public static class FabricNameHelper
{
    public static string BuildStableName(string prefix, string value, int maxLength = 15)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        var name = $"{prefix}{hash}";
        return name[..Math.Min(name.Length, maxLength)];
    }
}
