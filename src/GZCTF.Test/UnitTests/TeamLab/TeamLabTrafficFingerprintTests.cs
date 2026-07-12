using System;
using System.Linq;
using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Infrastructure;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabTrafficFingerprintTests
{
    [Fact]
    public void Create_ProducesStableFingerprintAndSeparatesGenerations()
    {
        var capturedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z");
        var sample = new TeamLabNodeFlowSample(
            17, capturedAt, "10.10.0.2", 41234, "192.168.20.3", 80, "tcp", 512);

        var first = TeamLabTrafficEnvelope.Create(10, 1, 3, 4, Guid.NewGuid(), sample);
        var replay = TeamLabTrafficEnvelope.Create(10, 1, 3, 4, Guid.NewGuid(), sample);
        var nextGeneration = TeamLabTrafficEnvelope.Create(10, 2, 3, 4, null, sample);

        Assert.Equal(first.Fingerprint, replay.Fingerprint);
        Assert.NotEqual(first.Fingerprint, nextGeneration.Fingerprint);
        Assert.Equal("TCP", first.Protocol);
    }

    [Fact]
    public void CreateBatches_EnforcesSampleCountBoundary()
    {
        var envelopes = Enumerable.Range(1, TeamLabTrafficIngestionLimits.MaxBatchSamples + 1)
            .Select(index => CreateEnvelope(index))
            .ToArray();

        var batches = RedisTeamLabTrafficIngestor.CreateBatches(envelopes);

        Assert.Equal(2, batches.Count);
        Assert.Equal(TeamLabTrafficIngestionLimits.MaxBatchSamples, batches[0].Count);
        Assert.Single(batches[1]);
    }

    [Fact]
    public void LocalBuffer_DropsOldestWithoutBlockingProducer()
    {
        var buffer = new TeamLabTrafficLocalBuffer(2, new RedisTelemetry());

        var dropped = buffer.EnqueueRange([CreateEnvelope(1), CreateEnvelope(2), CreateEnvelope(3)]);
        var drained = buffer.Drain(10);

        Assert.Equal(1, dropped);
        Assert.Equal(1, buffer.DroppedCount);
        Assert.Equal([2, 3], drained.Select(item => item.SourcePort).ToArray());
    }

    private static TeamLabTrafficEnvelope CreateEnvelope(int cursor)
    {
        var sample = new TeamLabNodeFlowSample(
            cursor,
            DateTimeOffset.UnixEpoch.AddSeconds(cursor),
            "10.0.0.2",
            cursor,
            "10.0.1.3",
            80,
            "tcp",
            128);
        return TeamLabTrafficEnvelope.Create(1, 1, 1, 1, Guid.Empty, sample);
    }
}
