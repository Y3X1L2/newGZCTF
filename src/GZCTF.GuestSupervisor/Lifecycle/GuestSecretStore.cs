using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed class GuestSecretStore(string stateRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _keyPath = Path.Combine(stateRoot, "secrets.key");
    private readonly string _statePath = Path.Combine(stateRoot, "secrets.state");

    public async Task MaterializeAsync(
        IReadOnlyList<GuestSecretValue> secrets,
        CancellationToken cancellationToken)
    {
        if (secrets.Count == 0) return;
        var values = secrets.ToDictionary(item => item.Reference, item => item.Value, StringComparer.Ordinal);
        var payload = JsonSerializer.SerializeToUtf8Bytes(values, JsonOptions);
        var key = await LoadKeyAsync(cancellationToken);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[payload.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length))
            aes.Encrypt(nonce, payload, cipher, tag);
        await AtomicWriteAsync(_statePath,
            JsonSerializer.SerializeToUtf8Bytes(new EncryptedState(cipher, nonce, tag), JsonOptions),
            cancellationToken);

        foreach (var secret in secrets)
        {
            var path = ValidateTargetPath(secret.TargetPath);
            await AtomicWriteAsync(path, System.Text.Encoding.UTF8.GetBytes(secret.Value), cancellationToken);
            Restrict(path);
        }
    }

    public void Cleanup(IReadOnlyList<GuestSecretReference> references)
    {
        foreach (var reference in references)
        {
            try
            {
                var path = ValidateTargetPath(reference.TargetPath);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (ArgumentException) { }
        }
        if (File.Exists(_statePath)) File.Delete(_statePath);
        if (File.Exists(_keyPath)) File.Delete(_keyPath);
    }

    private async Task<byte[]> LoadKeyAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_keyPath)) return await File.ReadAllBytesAsync(_keyPath, cancellationToken);
        var key = RandomNumberGenerator.GetBytes(32);
        await AtomicWriteAsync(_keyPath, key, cancellationToken);
        Restrict(_keyPath);
        return key;
    }

    private static string ValidateTargetPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathFullyQualified(targetPath) ||
            targetPath.Contains("..", StringComparison.Ordinal) ||
            targetPath.Contains("guest-supervisor", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("guest_secret_target_invalid", nameof(targetPath));
        return targetPath;
    }

    private static async Task AtomicWriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return;
        }
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                path,
                "/inheritance:r",
                "/grant:r",
                "*S-1-5-18:F",
                "*S-1-5-32-544:F"
            }
        });
        process?.WaitForExit();
        if (process is null || process.ExitCode != 0)
            throw new InvalidOperationException("guest_secret_acl_failed");
    }

    private sealed record EncryptedState(byte[] Ciphertext, byte[] Nonce, byte[] Tag);
}
