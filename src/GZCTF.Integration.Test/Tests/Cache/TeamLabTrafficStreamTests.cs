using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Cache;

public sealed class TeamLabTrafficStreamTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private RedisConnectionProvider? _connections;

    public Task InitializeAsync() => _redis.StartAsync();

    public async Task DisposeAsync()
    {
        if (_connections is not null)
            await _connections.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task PendingEntry_IsReclaimedAndAcknowledgedByAnotherConsumer()
    {
        var options = Options.Create(new RedisRuntimeOptions
        {
            Mode = RedisRuntimeMode.Distributed,
            ConnectionString = _redis.GetConnectionString(),
            KeyPrefix = $"phase5-{Guid.NewGuid():N}",
            ClientName = "phase5-stream-test"
        });
        var telemetry = new RedisTelemetry();
        var runtimeState = new RedisRuntimeState(options, telemetry);
        _connections = new RedisConnectionProvider(
            options, runtimeState, telemetry, NullLogger<RedisConnectionProvider>.Instance);
        var ingestor = new RedisTeamLabTrafficIngestor(
            _connections,
            new RedisKeyspace(options),
            runtimeState,
            telemetry,
            NullLogger<RedisTeamLabTrafficIngestor>.Instance);
        var sample = Observation(1);
        var envelope = TeamLabTrafficEnvelope.Create(
            10, 2, 3, 4, 5, TeamLabObservationPointKind.NetworkBridge, null, Guid.NewGuid(), sample);

        var enqueue = await ingestor.EnqueueAsync([envelope], CancellationToken.None);
        var first = await ingestor.ReadAsync("consumer-a", 10, TimeSpan.FromMilliseconds(50),
            CancellationToken.None);
        await Task.Delay(100);
        var reclaimed = await ingestor.ReadAsync("consumer-b", 10, TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        Assert.Equal(1, enqueue.AcceptedCount);
        Assert.Single(first.Messages);
        var message = Assert.Single(reclaimed.Messages);
        Assert.Equal(envelope.EvidenceFingerprint, message.Envelope.EvidenceFingerprint);
        Assert.NotNull(message.StreamId);
        await ingestor.AcknowledgeAsync([message.StreamId!], CancellationToken.None);
    }

    [Fact]
    public async Task ReclaimCursor_AdvancesPastRecentlyClaimedPendingEntries()
    {
        var ingestor = CreateIngestor();
        var workerNodeId = Guid.NewGuid();
        var envelopes = Enumerable.Range(1, 30)
            .Select(cursor => TeamLabTrafficEnvelope.Create(
                10, 2, 3, 4, 5, TeamLabObservationPointKind.NetworkBridge, null, workerNodeId,
                Observation(cursor)))
            .ToArray();
        await ingestor.EnqueueAsync(envelopes, CancellationToken.None);
        var pending = await ingestor.ReadAsync("consumer-a", envelopes.Length, TimeSpan.FromMilliseconds(50),
            CancellationToken.None);
        Assert.Equal(envelopes.Length, pending.Messages.Count);
        await Task.Delay(100);

        var reclaimedIds = new List<string>();
        for (var iteration = 0; iteration < envelopes.Length; iteration++)
        {
            var reclaimed = await ingestor.ReadAsync("consumer-b", 1, TimeSpan.FromMilliseconds(50),
                CancellationToken.None);
            var message = Assert.Single(reclaimed.Messages);
            reclaimedIds.Add(Assert.IsType<string>(message.StreamId));
        }

        Assert.Equal(envelopes.Length, reclaimedIds.Distinct().Count());
        await ingestor.AcknowledgeAsync(reclaimedIds, CancellationToken.None);
    }

    private RedisTeamLabTrafficIngestor CreateIngestor()
    {
        var options = Options.Create(new RedisRuntimeOptions
        {
            Mode = RedisRuntimeMode.Distributed,
            ConnectionString = _redis.GetConnectionString(),
            KeyPrefix = $"phase5-{Guid.NewGuid():N}",
            ClientName = "phase5-stream-test"
        });
        var telemetry = new RedisTelemetry();
        var runtimeState = new RedisRuntimeState(options, telemetry);
        _connections = new RedisConnectionProvider(
            options, runtimeState, telemetry, NullLogger<RedisConnectionProvider>.Instance);
        return new RedisTeamLabTrafficIngestor(
            _connections,
            new RedisKeyspace(options),
            runtimeState,
            telemetry,
            NullLogger<RedisTeamLabTrafficIngestor>.Instance);
    }

    private static TeamLabNodeObservationRecord Observation(long sequence) => new(
        sequence,
        Guid.NewGuid(),
        null,
        DateTimeOffset.UtcNow,
        "10.1.0.2",
        45000,
        "192.168.1.10",
        443,
        "tcp",
        null,
        256,
        "sha256:" + new string('b', 64),
        "sha256:" + new string('a', 64),
        "Packet",
        null,
        "observed");
}
