using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.GuestTelemetry.Contracts;
using GZCTF.EndpointSensor.Serialization;

namespace GZCTF.EndpointSensor.Security;

public sealed class SensorEventSigner(byte[] key)
{
    private readonly byte[] _key = key.Length >= 32
        ? key.ToArray()
        : throw new ArgumentException("Sensor HMAC key must contain at least 32 bytes.", nameof(key));

    public SensorEvent Sign(SensorEvent value)
    {
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(_key, Canonicalize(value)));
        return value with { Signature = signature };
    }

    public static byte[] Canonicalize(SensorEvent value) => JsonSerializer.SerializeToUtf8Bytes(
        new SensorCanonicalEvent(
            value.SchemaVersion,
            value.RuntimePublicId,
            value.Generation,
            value.AssetKey,
            value.Sequence,
            value.ObservedAt.ToUniversalTime().ToUnixTimeMilliseconds(),
            (byte)value.Kind,
            value.Process.ProcessId,
            value.Process.Name,
            value.Process.StartedAt.ToUniversalTime().ToUnixTimeMilliseconds(),
            value.Local.Address,
            value.Local.Port,
            value.Local.Protocol,
            value.Remote.Address,
            value.Remote.Port,
            value.Remote.Protocol),
        SensorJsonContext.Default.SensorCanonicalEvent);
}
