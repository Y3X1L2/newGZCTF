using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace GZCTF.Infrastructure.Cache;

public enum RedisRuntimeComponent
{
    Connection,
    Cache,
    Backplane,
    Stream
}

public enum RedisRuntimeStatus
{
    Disabled,
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

public sealed record RedisRuntimeComponentSnapshot(
    RedisRuntimeComponent Component,
    RedisRuntimeStatus Status,
    DateTimeOffset UpdatedAt,
    string? ReasonCode = null,
    TimeSpan? Lag = null);

public sealed class RedisRuntimeState
{
    private static readonly TimeSpan CacheProbeInterval = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<RedisRuntimeComponent, RedisRuntimeComponentSnapshot> _components = new();
    private readonly RedisRuntimeOptions _options;
    private readonly RedisTelemetry _telemetry;

    public RedisRuntimeState(IOptions<RedisRuntimeOptions> options, RedisTelemetry telemetry)
    {
        _options = options.Value;
        _telemetry = telemetry;
        var initial = _options.Mode == RedisRuntimeMode.Disabled ||
                      (_options.Mode == RedisRuntimeMode.SingleInstance &&
                       string.IsNullOrWhiteSpace(_options.ConnectionString))
            ? RedisRuntimeStatus.Disabled
            : RedisRuntimeStatus.Unknown;

        foreach (var component in Enum.GetValues<RedisRuntimeComponent>())
            Set(component, initial);
    }

    public RedisRuntimeMode Mode => _options.Mode;

    public bool ShouldBypassCache
    {
        get
        {
            if (_options.Mode == RedisRuntimeMode.Disabled ||
                (_options.Mode == RedisRuntimeMode.SingleInstance &&
                 string.IsNullOrWhiteSpace(_options.ConnectionString)))
                return false;

            if (Get(RedisRuntimeComponent.Connection).Status is RedisRuntimeStatus.Degraded or
                RedisRuntimeStatus.Unhealthy)
                return true;

            var cache = Get(RedisRuntimeComponent.Cache);
            return cache.Status is RedisRuntimeStatus.Degraded or RedisRuntimeStatus.Unhealthy &&
                   DateTimeOffset.UtcNow - cache.UpdatedAt < CacheProbeInterval;
        }
    }

    public IReadOnlyDictionary<RedisRuntimeComponent, RedisRuntimeComponentSnapshot> Snapshot() =>
        _components.ToDictionary(item => item.Key, item => item.Value);

    public RedisRuntimeComponentSnapshot Get(RedisRuntimeComponent component) => _components[component];

    public void MarkConnectionAvailable(bool reconnected = false)
    {
        var changed = Get(RedisRuntimeComponent.Connection).Status != RedisRuntimeStatus.Healthy;
        Set(RedisRuntimeComponent.Connection, RedisRuntimeStatus.Healthy);
        if (changed || reconnected)
            _telemetry.RecordOperation(RedisTelemetryPurpose.Connection,
                reconnected ? RedisTelemetryStatus.Reconnected : RedisTelemetryStatus.Success);
    }

    public void MarkConnectionUnavailable(string reasonCode)
    {
        var status = FailureStatus();
        var reason = NormalizeReason(reasonCode);
        var current = Get(RedisRuntimeComponent.Connection);
        Set(RedisRuntimeComponent.Connection, status, reason);
        if (current.Status != status || !string.Equals(current.ReasonCode, reason, StringComparison.Ordinal))
            _telemetry.RecordOperation(RedisTelemetryPurpose.Connection, RedisTelemetryStatus.Failure);
    }

    public void RecordSuccess(string purpose)
    {
        var component = ComponentFor(purpose);
        Set(component, RedisRuntimeStatus.Healthy);
        _telemetry.RecordOperation(TelemetryPurposeFor(component), RedisTelemetryStatus.Success);
    }

    public void RecordFailure(string purpose, string reasonCode = "operation-failed")
    {
        var component = ComponentFor(purpose);
        Set(component, FailureStatus(), NormalizeReason(reasonCode));
        _telemetry.RecordOperation(TelemetryPurposeFor(component), RedisTelemetryStatus.Failure);
    }

    public void SetStreamConsumerLag(TimeSpan lag)
    {
        var normalized = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
        var status = normalized > _options.StreamLagWarningThreshold
            ? RedisRuntimeStatus.Degraded
            : RedisRuntimeStatus.Healthy;
        Set(RedisRuntimeComponent.Stream, status, lag: normalized);
        _telemetry.SetStreamConsumerLag(normalized,
            status == RedisRuntimeStatus.Healthy
                ? RedisTelemetryStatus.Success
                : RedisTelemetryStatus.Failure);
    }

    private RedisRuntimeStatus FailureStatus() => _options.Mode == RedisRuntimeMode.Distributed
        ? RedisRuntimeStatus.Unhealthy
        : RedisRuntimeStatus.Degraded;

    private void Set(RedisRuntimeComponent component, RedisRuntimeStatus status, string? reasonCode = null,
        TimeSpan? lag = null) =>
        _components[component] = new(component, status, DateTimeOffset.UtcNow, reasonCode, lag);

    private static RedisRuntimeComponent ComponentFor(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return purpose.Trim().ToLowerInvariant() switch
        {
            "cache" or "cache-invalidation" => RedisRuntimeComponent.Cache,
            "backplane" or "signalr" => RedisRuntimeComponent.Backplane,
            "stream" or "stream-consumer" => RedisRuntimeComponent.Stream,
            "connection" => RedisRuntimeComponent.Connection,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown Redis runtime purpose.")
        };
    }

    private static RedisTelemetryPurpose TelemetryPurposeFor(RedisRuntimeComponent component) => component switch
    {
        RedisRuntimeComponent.Connection => RedisTelemetryPurpose.Connection,
        RedisRuntimeComponent.Cache => RedisTelemetryPurpose.Cache,
        RedisRuntimeComponent.Backplane => RedisTelemetryPurpose.Backplane,
        RedisRuntimeComponent.Stream => RedisTelemetryPurpose.Stream,
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, null)
    };

    private static string NormalizeReason(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        var normalized = reasonCode.Trim().ToLowerInvariant();
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }
}
