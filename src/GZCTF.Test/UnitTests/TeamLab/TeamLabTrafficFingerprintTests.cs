using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabTrafficFingerprintTests
{
    [Fact]
    public void Create_ProducesStableFingerprintAndSeparatesGenerations()
    {
        var capturedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z");
        var sample = Observation(
            17, capturedAt, "10.10.0.2", 41234, "192.168.20.3", 80, "tcp", 512);

        var workerNodeId = Guid.NewGuid();
        var first = TeamLabTrafficEnvelope.Create(
            10, 1, 3, 4, 5, TeamLabObservationPointKind.NetworkBridge, null, workerNodeId, sample);
        var replay = TeamLabTrafficEnvelope.Create(
            10, 1, 3, 4, 5, TeamLabObservationPointKind.NetworkBridge, null, workerNodeId, sample);
        var nextGeneration = TeamLabTrafficEnvelope.Create(
            10, 2, 3, 4, 5, TeamLabObservationPointKind.NetworkBridge, null, workerNodeId, sample);

        Assert.Equal(first.EvidenceFingerprint, replay.EvidenceFingerprint);
        Assert.NotEqual(first.EvidenceFingerprint, nextGeneration.EvidenceFingerprint);
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
    public async Task RedisUnavailable_DefersInsteadOfAcknowledgingVolatileMemory()
    {
        var options = Options.Create(new RedisRuntimeOptions { Mode = RedisRuntimeMode.Disabled });
        var telemetry = new RedisTelemetry();
        var state = new RedisRuntimeState(options, telemetry);
        await using var connections = new RedisConnectionProvider(
            options, state, telemetry, NullLogger<RedisConnectionProvider>.Instance);
        var ingestor = new RedisTeamLabTrafficIngestor(
            connections,
            new RedisKeyspace(options),
            new TeamLabTrafficLocalBuffer(telemetry),
            state,
            telemetry,
            NullLogger<RedisTeamLabTrafficIngestor>.Instance);

        var result = await ingestor.EnqueueAsync([CreateEnvelope(1)], CancellationToken.None);

        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(0, result.DroppedCount);
        Assert.True(result.Deferred);
    }

    [Fact]
    public void PathCorrelation_UsesExactPacketEvidenceAcrossOrderedObservationPoints()
    {
        var packet = Enumerable.Repeat((byte)0x42, 32).ToArray();
        var start = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        var observations = new[]
        {
            StoredObservation(1, 11, start, packet, null, 1),
            StoredObservation(2, 12, start.AddMilliseconds(2), packet, null, 1),
            StoredObservation(3, 13, start.AddMilliseconds(4), packet, null, 1)
        };

        var path = Assert.Single(TeamLabTrafficPathCorrelator.BuildPacketPaths(7, 2, observations));

        Assert.Equal(TeamLabPathConfidence.PacketExact, path.Confidence);
        Assert.Equal([1L, 2L, 3L], path.Hops.Select(item => item.ObservationId).ToArray());
        Assert.Equal([11, 12, 13], path.Hops.Select(item => item.ObservationPointId).ToArray());
    }

    [Fact]
    public void PathCorrelation_PreservesRepeatedObservationPointsInRoundTripOrder()
    {
        var packet = Enumerable.Repeat((byte)0x43, 32).ToArray();
        var start = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        var observations = new[]
        {
            StoredObservation(1, 11, start, packet, null, 1),
            StoredObservation(2, 12, start.AddMilliseconds(1), packet, null, 1),
            StoredObservation(3, 13, start.AddMilliseconds(2), packet, null, 1),
            StoredObservation(4, 12, start.AddMilliseconds(3), packet, null, 1),
            StoredObservation(5, 11, start.AddMilliseconds(4), packet, null, 1)
        };

        var path = Assert.Single(TeamLabTrafficPathCorrelator.BuildPacketPaths(7, 2, observations));

        Assert.Equal([11, 12, 13, 12, 11], path.Hops.Select(item => item.ObservationPointId).ToArray());
        Assert.Equal([1L, 2L, 3L, 4L, 5L], path.Hops.Select(item => item.ObservationId).ToArray());
    }

    [Fact]
    public void PathCorrelation_FingerprintIncludesTheConcreteObservationIds()
    {
        var start = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        var firstPacket = Enumerable.Repeat((byte)0x11, 32).ToArray();
        var secondPacket = Enumerable.Repeat((byte)0x22, 32).ToArray();
        var paths = TeamLabTrafficPathCorrelator.BuildPacketPaths(7, 2,
        [
            StoredObservation(1, 11, start, firstPacket, null, 1),
            StoredObservation(2, 12, start.AddMilliseconds(1), firstPacket, null, 1),
            StoredObservation(3, 11, start.AddMilliseconds(2), secondPacket, null, 2),
            StoredObservation(4, 12, start.AddMilliseconds(3), secondPacket, null, 2)
        ]);

        Assert.Equal(2, paths.Count);
        Assert.NotEqual(paths[0].EvidenceFingerprint, paths[1].EvidenceFingerprint);
    }

    [Fact]
    public void PathCorrelation_LabelsSocketSnapshotsAsTemporalRatherThanCausal()
    {
        var process = Enumerable.Repeat((byte)0x24, 32).ToArray();
        var start = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        var observations = new[]
        {
            StoredObservation(10, 21, start, null, process, 1),
            StoredObservation(11, 21, start.AddSeconds(1), null, process, 2)
        };

        var path = Assert.Single(TeamLabTrafficPathCorrelator.BuildTemporalProcessPaths(7, 2, observations));

        Assert.Equal(TeamLabPathConfidence.TemporallyRelated, path.Confidence);
        Assert.NotEqual(TeamLabPathConfidence.ProcessCorrelated, path.Confidence);
    }

    [Fact]
    public void PathCorrelation_LabelsDirectedSameProcessEventsAsProcessCorrelated()
    {
        var process = Enumerable.Repeat((byte)0x25, 32).ToArray();
        var start = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        var observations = new[]
        {
            StoredObservation(20, 21, start, null, process, 1, "accepted"),
            StoredObservation(21, 21, start.AddMilliseconds(10), null, process, 2, "connected")
        };

        var path = Assert.Single(TeamLabTrafficPathCorrelator.BuildTemporalProcessPaths(7, 2, observations));

        Assert.Equal(TeamLabPathConfidence.ProcessCorrelated, path.Confidence);
        Assert.Equal([20L, 21L], path.Hops.Select(item => item.ObservationId).ToArray());
    }

    [Fact]
    public void CorrelationCursor_AdvancesMonotonicallyAtVisibleTail()
    {
        var observations = new[]
        {
            StoredObservation(101, 11, DateTimeOffset.UnixEpoch, Enumerable.Repeat((byte)1, 32).ToArray(), null, 1),
            StoredObservation(102, 12, DateTimeOffset.UnixEpoch.AddMilliseconds(1), Enumerable.Repeat((byte)1, 32).ToArray(), null, 1)
        };

        Assert.Equal(102, TeamLabTrafficPathCorrelator.NextScanCursor(observations, 100));
        Assert.Equal(120, TeamLabTrafficPathCorrelator.NextScanCursor(observations, 120));
        Assert.Equal(120, TeamLabTrafficPathCorrelator.NextScanCursor([], 120));
    }

    [Fact]
    public void ObservationPreparation_StopsCursorAtFirstUnresolvedRecord()
    {
        var records = new[]
        {
            Observation(11, DateTimeOffset.UnixEpoch, "10.0.0.2", 1, "10.0.0.3", 2, "tcp", 64),
            Observation(12, DateTimeOffset.UnixEpoch.AddMilliseconds(1), "10.0.0.2", 1, "10.0.0.3", 2, "tcp", 64),
            Observation(13, DateTimeOffset.UnixEpoch.AddMilliseconds(2), "10.0.0.2", 1, "10.0.0.3", 2, "tcp", 64)
        };
        var workerNodeId = Guid.NewGuid();

        var prepared = TeamLabTrafficApplicationService.PrepareObservationBatch(
            records,
            10,
            13,
            record => record.Sequence == 12
                ? null
                : TeamLabTrafficEnvelope.Create(
                    7, 2, 3, 4, 5, TeamLabObservationPointKind.NetworkBridge, null, workerNodeId, record));

        Assert.Single(prepared.Envelopes);
        Assert.Equal(11, prepared.NextSequence);
        Assert.True(prepared.BlockedByUnresolvedRecord);
    }

    private static TeamLabTrafficEnvelope CreateEnvelope(int cursor)
    {
        var sample = Observation(
            cursor,
            DateTimeOffset.UnixEpoch.AddSeconds(cursor),
            "10.0.0.2",
            cursor,
            "10.0.1.3",
            80,
            "tcp",
            128);
        return TeamLabTrafficEnvelope.Create(
            1, 1, 1, 1, 1, TeamLabObservationPointKind.NetworkBridge, null, Guid.NewGuid(), sample);
    }

    private static TeamLabTrafficObservation StoredObservation(
        long id,
        int observationPointId,
        DateTimeOffset observedAt,
        byte[]? packetFingerprint,
        byte[]? processIdentityHash,
        byte flowMarker,
        string direction = "observed") => new()
    {
        Id = id,
        RuntimeId = 7,
        Generation = 2,
        ObservationPointId = observationPointId,
        WorkerNodeId = Guid.NewGuid(),
        SourceSequence = id,
        ObservedAt = observedAt,
        SourceIp = "10.0.0.2",
        SourcePort = 40000,
        DestinationIp = "10.0.1.3",
        DestinationPort = 443,
        Protocol = "TCP",
        PacketLength = 128,
        PacketFingerprint = packetFingerprint,
        FlowFingerprint = Enumerable.Repeat(flowMarker, 32).ToArray(),
        ProcessIdentityHash = processIdentityHash,
        EvidenceKind = packetFingerprint is null
            ? TeamLabTrafficEvidenceKind.EndpointProcess
            : TeamLabTrafficEvidenceKind.Packet,
        Direction = direction
    };

    private static TeamLabNodeObservationRecord Observation(
        long sequence,
        DateTimeOffset capturedAt,
        string sourceIp,
        int? sourcePort,
        string destinationIp,
        int? destinationPort,
        string protocol,
        int packetLength) => new(
        sequence,
        Guid.NewGuid(),
        null,
        capturedAt,
        sourceIp,
        sourcePort,
        destinationIp,
        destinationPort,
        protocol,
        null,
        packetLength,
        null,
        "sha256:" + new string('a', 64),
        "Packet",
        null,
        "observed");
}
