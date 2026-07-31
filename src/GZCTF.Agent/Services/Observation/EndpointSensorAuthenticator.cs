using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GZCTF.Agent.Services.Observation;

public enum EndpointSensorEventKind : byte
{
    Opened = 0,
    Observed = 1,
    Closed = 2
}

public sealed record EndpointSensorProcessIdentity(
    int ProcessId,
    string Name,
    DateTimeOffset StartedAt);

public sealed record EndpointSensorEndpoint(
    string Address,
    int? Port,
    string Protocol);

public sealed record EndpointSensorEvent(
    int SchemaVersion,
    string RuntimePublicId,
    int Generation,
    string AssetKey,
    long Sequence,
    DateTimeOffset ObservedAt,
    EndpointSensorEventKind Kind,
    EndpointSensorProcessIdentity Process,
    EndpointSensorEndpoint Local,
    EndpointSensorEndpoint Remote,
    string Signature);

public sealed record EndpointSensorVerification(
    bool Success,
    string Code,
    string? ProcessIdentityHash = null,
    string? FlowFingerprint = null);

public static class EndpointSensorAuthenticator
{
    public static EndpointSensorVerification Verify(
        EndpointSensorEvent value,
        Guid expectedRuntimePublicId,
        int expectedGeneration,
        string expectedAssetKey,
        long previousSequence,
        ReadOnlySpan<byte> key,
        DateTimeOffset now)
    {
        if (value.SchemaVersion != 1)
            return Failed("sensor_schema_unsupported");
        if (!Guid.TryParse(value.RuntimePublicId, out var runtimePublicId) ||
            runtimePublicId != expectedRuntimePublicId || value.Generation != expectedGeneration ||
            !string.Equals(value.AssetKey, expectedAssetKey, StringComparison.Ordinal))
            return Failed("sensor_identity_mismatch");
        if (value.Sequence <= previousSequence)
            return Failed("sensor_sequence_replayed");
        if (value.ObservedAt < now.AddMinutes(-10) || value.ObservedAt > now.AddMinutes(2))
            return Failed("sensor_timestamp_invalid");
        if (value.Process.ProcessId <= 0 || value.Process.Name.Length is < 1 or > 128 ||
            value.Local.Address.Length is < 1 or > 64 || value.Remote.Address.Length is < 1 or > 64 ||
            value.Local.Protocol.Length is < 1 or > 16 || value.Remote.Protocol.Length is < 1 or > 16 ||
            value.Local.Port is < 0 or > ushort.MaxValue || value.Remote.Port is < 0 or > ushort.MaxValue)
            return Failed("sensor_payload_invalid");
        if (value.Signature.Length != 64 || !value.Signature.All(Uri.IsHexDigit))
            return Failed("sensor_signature_invalid");
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(value.Signature);
        }
        catch (FormatException)
        {
            return Failed("sensor_signature_invalid");
        }
        var expected = HMACSHA256.HashData(key, Canonicalize(value));
        if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
            return Failed("sensor_signature_invalid");
        var processIdentity = Digest(Encoding.UTF8.GetBytes(
            $"{value.Process.ProcessId}|{value.Process.Name}|{value.Process.StartedAt.ToUniversalTime().ToUnixTimeMilliseconds()}"));
        var flow = Digest(Encoding.UTF8.GetBytes(
            $"{value.Local.Address}|{value.Local.Port?.ToString() ?? ""}|{value.Remote.Address}|{value.Remote.Port?.ToString() ?? ""}|{value.Local.Protocol.ToUpperInvariant()}"));
        return new EndpointSensorVerification(true, "accepted", processIdentity, flow);
    }

    public static byte[] Canonicalize(EndpointSensorEvent value) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        value.SchemaVersion,
        value.RuntimePublicId,
        value.Generation,
        value.AssetKey,
        value.Sequence,
        ObservedAt = value.ObservedAt.ToUniversalTime().ToUnixTimeMilliseconds(),
        Kind = (byte)value.Kind,
        value.Process.ProcessId,
        ProcessName = value.Process.Name,
        ProcessStartedAt = value.Process.StartedAt.ToUniversalTime().ToUnixTimeMilliseconds(),
        LocalAddress = value.Local.Address,
        LocalPort = value.Local.Port,
        LocalProtocol = value.Local.Protocol,
        RemoteAddress = value.Remote.Address,
        RemotePort = value.Remote.Port,
        RemoteProtocol = value.Remote.Protocol
    });

    private static EndpointSensorVerification Failed(string code) => new(false, code);

    private static string Digest(byte[] value) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(value))}";
}
