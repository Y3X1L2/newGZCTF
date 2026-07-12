using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GZCTF.Infrastructure.Cache;

public enum RedisTelemetryPurpose
{
    Connection,
    Cache,
    Lock,
    Lease,
    Stream,
    Backplane,
    WakeUp
}

public enum RedisTelemetryStatus
{
    Success,
    Failure,
    Bypassed,
    Reconnected,
    Dropped
}

public sealed class RedisTelemetry : IDisposable
{
    public const string MeterName = "GZCTF.RedisRuntime";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _operations;
    private readonly Histogram<double> _duration;
    private long _streamLagMilliseconds;
    private int _streamLagStatus = (int)RedisTelemetryStatus.Success;

    public RedisTelemetry()
    {
        _operations = _meter.CreateCounter<long>("gzctf_redis_operations_total", "operations");
        _duration = _meter.CreateHistogram<double>("gzctf_redis_operation_duration_seconds", "s");
        _meter.CreateObservableGauge("gzctf_redis_stream_consumer_lag_milliseconds", ObserveStreamLag, "ms");
    }

    public void RecordOperation(RedisTelemetryPurpose purpose, RedisTelemetryStatus status,
        TimeSpan? duration = null)
    {
        var tags = Tags(purpose, status);
        _operations.Add(1, tags);
        if (duration is { } elapsed)
            _duration.Record(elapsed.TotalSeconds, tags);
    }

    public void SetStreamConsumerLag(TimeSpan lag, RedisTelemetryStatus status)
    {
        var milliseconds = Math.Max(0, (long)lag.TotalMilliseconds);
        Interlocked.Exchange(ref _streamLagMilliseconds, milliseconds);
        Volatile.Write(ref _streamLagStatus, (int)status);
    }

    public void Dispose() => _meter.Dispose();

    private Measurement<long> ObserveStreamLag() => new(
        Interlocked.Read(ref _streamLagMilliseconds),
        Tags(RedisTelemetryPurpose.Stream, (RedisTelemetryStatus)Volatile.Read(ref _streamLagStatus)));

    private static TagList Tags(RedisTelemetryPurpose purpose, RedisTelemetryStatus status) => new()
    {
        { "purpose", PurposeName(purpose) },
        { "status", StatusName(status) }
    };

    private static string PurposeName(RedisTelemetryPurpose purpose) => purpose switch
    {
        RedisTelemetryPurpose.Connection => "connection",
        RedisTelemetryPurpose.Cache => "cache",
        RedisTelemetryPurpose.Lock => "lock",
        RedisTelemetryPurpose.Lease => "lease",
        RedisTelemetryPurpose.Stream => "stream",
        RedisTelemetryPurpose.Backplane => "backplane",
        RedisTelemetryPurpose.WakeUp => "wake-up",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    private static string StatusName(RedisTelemetryStatus status) => status switch
    {
        RedisTelemetryStatus.Success => "success",
        RedisTelemetryStatus.Failure => "failure",
        RedisTelemetryStatus.Bypassed => "bypassed",
        RedisTelemetryStatus.Reconnected => "reconnected",
        RedisTelemetryStatus.Dropped => "dropped",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
