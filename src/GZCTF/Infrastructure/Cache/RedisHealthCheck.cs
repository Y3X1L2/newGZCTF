using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GZCTF.Infrastructure.Cache;

public sealed class RedisHealthCheck(
    IRedisConnectionProvider connectionProvider,
    RedisRuntimeState runtimeState,
    IOptions<RedisRuntimeOptions> options) : IHealthCheck
{
    private readonly RedisRuntimeOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_options.Mode == RedisRuntimeMode.Disabled ||
            (_options.Mode == RedisRuntimeMode.SingleInstance && !connectionProvider.IsConfigured))
            return HealthCheckResult.Healthy("Redis is optional and local runtime services remain available.",
                BuildData(connected: false));

        try
        {
            var connection = await connectionProvider.GetAsync(cancellationToken);
            if (connection is null || !connection.IsConnected)
            {
                runtimeState.MarkConnectionUnavailable("health-check-disconnected");
                return _options.Mode == RedisRuntimeMode.Distributed
                    ? HealthCheckResult.Unhealthy("Redis is required but not connected.", data: BuildData(false))
                    : HealthCheckResult.Degraded("Redis is unavailable; single-instance fallbacks remain active.",
                        data: BuildData(false));
            }

            runtimeState.MarkConnectionAvailable();
            var snapshots = runtimeState.Snapshot().Values;
            if (snapshots.Any(item => item.Status == RedisRuntimeStatus.Unhealthy))
                return HealthCheckResult.Degraded("Redis is connected but one or more runtime components are unhealthy.",
                    data: BuildData(true));
            if (snapshots.Any(item => item.Status == RedisRuntimeStatus.Degraded))
                return HealthCheckResult.Degraded("Redis is connected but one or more runtime components are degraded.",
                    data: BuildData(true));

            return HealthCheckResult.Healthy("Redis runtime is healthy.", BuildData(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            runtimeState.MarkConnectionUnavailable("health-check-failed");
            return _options.Mode == RedisRuntimeMode.Distributed
                ? HealthCheckResult.Unhealthy("Redis readiness check failed.", exception, BuildData(false))
                : HealthCheckResult.Degraded("Redis health check failed; single-instance fallbacks remain active.",
                    exception, BuildData(false));
        }
    }

    private IReadOnlyDictionary<string, object> BuildData(bool connected)
    {
        var snapshots = runtimeState.Snapshot();
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["mode"] = _options.Mode.ToString(),
            ["connected"] = connected,
            ["connection"] = Status(snapshots[RedisRuntimeComponent.Connection]),
            ["cache"] = Status(snapshots[RedisRuntimeComponent.Cache]),
            ["backplane"] = Status(snapshots[RedisRuntimeComponent.Backplane]),
            ["stream"] = Status(snapshots[RedisRuntimeComponent.Stream]),
            ["streamLagMilliseconds"] = snapshots[RedisRuntimeComponent.Stream].Lag?.TotalMilliseconds ?? 0
        };
    }

    private static string Status(RedisRuntimeComponentSnapshot snapshot) =>
        snapshot.Status.ToString().ToLowerInvariant();
}
