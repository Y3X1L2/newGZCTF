using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using GZCTF.Infrastructure.Cache;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Cache;

public class RedisRuntimeStateAndTelemetryTests
{
    [Fact]
    public void RuntimeState_DistributedFailureBypassesCacheAndTracksStreamLag()
    {
        var options = new RedisRuntimeOptions
        {
            Mode = RedisRuntimeMode.Distributed,
            ConnectionString = "localhost:6379",
            StreamLagWarningThreshold = TimeSpan.FromSeconds(2)
        };
        using var telemetry = new RedisTelemetry();
        var state = new RedisRuntimeState(Options.Create(options), telemetry);

        state.MarkConnectionAvailable();
        state.RecordFailure("cache", "timeout");
        state.SetStreamConsumerLag(TimeSpan.FromSeconds(3));

        Assert.True(state.ShouldBypassCache);
        Assert.Equal(RedisRuntimeStatus.Unhealthy, state.Get(RedisRuntimeComponent.Cache).Status);
        var stream = state.Get(RedisRuntimeComponent.Stream);
        Assert.Equal(RedisRuntimeStatus.Degraded, stream.Status);
        Assert.Equal(TimeSpan.FromSeconds(3), stream.Lag);
    }

    [Fact]
    public void RuntimeState_DisabledModeKeepsMemoryCacheAvailable()
    {
        using var telemetry = new RedisTelemetry();
        var state = new RedisRuntimeState(Options.Create(new RedisRuntimeOptions
        {
            Mode = RedisRuntimeMode.Disabled
        }), telemetry);

        Assert.False(state.ShouldBypassCache);
        Assert.All(state.Snapshot().Values,
            snapshot => Assert.Equal(RedisRuntimeStatus.Disabled, snapshot.Status));
    }

    [Fact]
    public void Telemetry_UsesOnlyPurposeAndStatusLabels()
    {
        var measurements = new ConcurrentQueue<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == RedisTelemetry.MeterName &&
                    instrument.Name == "gzctf_redis_operations_total")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Enqueue(tags.ToArray()));
        listener.Start();
        using var telemetry = new RedisTelemetry();

        telemetry.RecordOperation(RedisTelemetryPurpose.Lease, RedisTelemetryStatus.Failure,
            TimeSpan.FromMilliseconds(25));

        var tags = Assert.Single(measurements);
        Assert.Equal(["purpose", "status"], tags.Select(tag => tag.Key).Order().ToArray());
        Assert.Contains(tags, tag => tag.Key == "purpose" && Equals(tag.Value, "lease"));
        Assert.Contains(tags, tag => tag.Key == "status" && Equals(tag.Value, "failure"));
    }
}
