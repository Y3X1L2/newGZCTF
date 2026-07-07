using System.Security.Cryptography;
using System.Text;

namespace GZCTF.Utils;

public static class SubmissionLogRedactor
{
    public static string RedactAnswer(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return "empty";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(answer));
        return $"sha256:{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }
}
