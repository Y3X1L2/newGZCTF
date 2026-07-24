using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed record GuestLocalCheckpoint(
    GuestAssetIdentity Identity,
    GuestLifecycleStage? Stage,
    long Sequence,
    string IntentDigest,
    string? PayloadDigest,
    bool EmissionAcknowledged,
    string BootIdentity,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore] bool BootChanged = false,
    GuestLifecycleOutcome Outcome = GuestLifecycleOutcome.Ready,
    string? ErrorCode = null);

public sealed class GuestCheckpointStore(string stateRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(stateRoot, "checkpoint.json");

    public async Task<GuestLocalCheckpoint> LoadAsync(
        GuestAssetIdentity identity,
        string intentDigest,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var bootIdentity = CurrentBootIdentity();
            if (!File.Exists(_path))
                return new GuestLocalCheckpoint(
                    identity, null, 0, intentDigest, null, true, bootIdentity, DateTimeOffset.UtcNow);
            await using var stream = File.OpenRead(_path);
            var current = await JsonSerializer.DeserializeAsync<GuestLocalCheckpoint>(
                stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("guest_checkpoint_invalid");
            GuestControlContractValidator.ValidateIdentity(identity, current.Identity, requireBootEpoch: false);
            if (!string.Equals(current.IntentDigest, intentDigest, StringComparison.Ordinal))
                throw new InvalidDataException("guest_checkpoint_intent_mismatch");
            if (!string.Equals(current.BootIdentity, bootIdentity, StringComparison.Ordinal))
                current = current with
                {
                    Identity = current.Identity with { BootEpoch = checked(current.Identity.BootEpoch + 1) },
                    BootIdentity = bootIdentity,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    BootChanged = true
                };
            return current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(GuestLocalCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary,
                    JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions), cancellationToken);
                await using (var stream = new FileStream(
                                 temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    stream.Flush(flushToDisk: true);
                File.Move(temporary, _path, true);
                Restrict(_path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CurrentBootIdentity()
    {
        if (!OperatingSystem.IsWindows() && File.Exists("/proc/sys/kernel/random/boot_id"))
            return File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        var bootTime = DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(bootTime.ToUnixTimeSeconds().ToString())));
    }

    internal static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
