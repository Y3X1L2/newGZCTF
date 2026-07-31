using System.Text.Json.Serialization;
using GZCTF.GuestTelemetry.Contracts;

namespace GZCTF.EndpointSensor.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(SensorEvent))]
[JsonSerializable(typeof(SensorCanonicalEvent))]
internal sealed partial class SensorJsonContext : JsonSerializerContext;

internal sealed record SensorCanonicalEvent(
    int SchemaVersion,
    string RuntimePublicId,
    int Generation,
    string AssetKey,
    long Sequence,
    long ObservedAt,
    byte Kind,
    int ProcessId,
    string ProcessName,
    long ProcessStartedAt,
    string LocalAddress,
    int? LocalPort,
    string LocalProtocol,
    string RemoteAddress,
    int? RemotePort,
    string RemoteProtocol);
