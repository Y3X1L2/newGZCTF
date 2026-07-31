using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed class GuestBootstrapExecutionStore(string stateRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _stateRoot = Path.Combine(stateRoot, "bootstrap");

    public async Task<GuestBootstrapExecutionState> LoadAsync(
        string intentDigest,
        string artifactDigest,
        CancellationToken cancellationToken)
    {
        var path = StatePath(intentDigest, artifactDigest);
        if (!File.Exists(path))
            return new GuestBootstrapExecutionState(intentDigest, artifactDigest, 0, new Dictionary<string, GuestStepState>(), []);
        await using var stream = File.OpenRead(path);
        var state = await JsonSerializer.DeserializeAsync<GuestBootstrapExecutionState>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("guest_bootstrap_state_invalid");
        if (!string.Equals(state.IntentDigest, intentDigest, StringComparison.Ordinal) ||
            !string.Equals(state.ArtifactDigest, artifactDigest, StringComparison.Ordinal))
            throw new InvalidDataException("guest_bootstrap_state_identity_mismatch");
        return state;
    }

    public async Task SaveAsync(GuestBootstrapExecutionState state, CancellationToken cancellationToken)
    {
        var path = StatePath(state.IntentDigest, state.ArtifactDigest);
        Directory.CreateDirectory(_stateRoot);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary,
                JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), cancellationToken);
            await using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, path, true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string StatePath(string intentDigest, string artifactDigest) =>
        Path.Combine(_stateRoot, $"{ShortDigest(intentDigest)}-{ShortDigest(artifactDigest)}.json");

    private static string ShortDigest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}

public sealed record GuestBootstrapExecutionState(
    string IntentDigest,
    string ArtifactDigest,
    int RebootCount,
    IReadOnlyDictionary<string, GuestStepState> Steps,
    IReadOnlyList<string> PassedHealthChecks);

public sealed record GuestStepState(
    string Status,
    int? ExitCode,
    string? OutputDigest,
    long RequestedBootEpoch,
    DateTimeOffset UpdatedAt);
