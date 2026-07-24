namespace GZCTF.GuestTelemetry.Contracts;

public enum SensorEventKind : byte
{
    Opened = 0,
    Observed = 1,
    Closed = 2
}

public sealed record SensorProcessIdentity(
    int ProcessId,
    string Name,
    DateTimeOffset StartedAt);

public sealed record SensorEndpoint(
    string Address,
    int? Port,
    string Protocol);

public sealed record SensorEvent(
    int SchemaVersion,
    string RuntimePublicId,
    int Generation,
    string AssetKey,
    long Sequence,
    DateTimeOffset ObservedAt,
    SensorEventKind Kind,
    SensorProcessIdentity Process,
    SensorEndpoint Local,
    SensorEndpoint Remote,
    string Signature);

public sealed record ConnectionSnapshot(
    SensorProcessIdentity Process,
    SensorEndpoint Local,
    SensorEndpoint Remote)
{
    public string Identity =>
        $"{Process.ProcessId}|{Process.StartedAt.ToUnixTimeMilliseconds()}|{Local.Protocol}|{Local.Address}|{Local.Port}|{Remote.Address}|{Remote.Port}";
}

public interface IConnectionProvider
{
    Task<IReadOnlyList<ConnectionSnapshot>> ReadAsync(CancellationToken cancellationToken);
}
