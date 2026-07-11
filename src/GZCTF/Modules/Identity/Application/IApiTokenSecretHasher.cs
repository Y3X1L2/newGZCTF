namespace GZCTF.Modules.Identity.Application;

public interface IApiTokenSecretHasher
{
    byte[] Hash(ReadOnlySpan<byte> secret);
    bool Verify(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> expectedHash);
}
