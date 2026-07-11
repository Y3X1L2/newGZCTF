using System.Security.Cryptography;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Identity.Infrastructure;

public sealed class ApiTokenSecretHasher : IApiTokenSecretHasher
{
    public byte[] Hash(ReadOnlySpan<byte> secret) => SHA256.HashData(secret);

    public bool Verify(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> expectedHash)
    {
        if (expectedHash.Length != SHA256.HashSizeInBytes)
            return false;

        Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(secret, actualHash);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
