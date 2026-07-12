using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.TeamLab.Application;
using StackExchange.Redis;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class RedisTeamLabTrafficIngestor(
    IRedisConnectionProvider connections,
    RedisKeyspace keyspace,
    TeamLabTrafficLocalBuffer localBuffer,
    RedisRuntimeState runtimeState,
    RedisTelemetry telemetry,
    ILogger<RedisTeamLabTrafficIngestor> logger) : ITeamLabTrafficIngestor
{
    public const string ConsumerGroup = "gzctf-teamlab-flow-v1";
    public const int MaxStreamLength = 250_000;

    private static readonly LuaScript ProtectedTrimScript = LuaScript.Prepare(
        "local p = redis.call('XPENDING', @stream, @group); " +
        "if p[1] == 0 then return redis.call('XTRIM', @stream, 'MAXLEN', '~', @maxLength); end; " +
        "return redis.call('XTRIM', @stream, 'MINID', '~', p[2]);");

    private readonly RedisKey _streamKey = keyspace.Create(RedisKeyPurpose.Stream, "teamlab-flow");
    private readonly SemaphoreSlim _groupGate = new(1, 1);
    private readonly ConcurrentDictionary<string, RedisValue> _reclaimCursors = new(StringComparer.Ordinal);
    private int _groupReady;

    public async ValueTask<TeamLabTrafficEnqueueResult> EnqueueAsync(
        IReadOnlyCollection<TeamLabTrafficEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        if (envelopes.Count == 0)
            return new TeamLabTrafficEnqueueResult(0, 0, 0, false);

        var batches = CreateBatches(envelopes);
        var connection = await TryGetConnectionAsync(cancellationToken);
        if (connection is null)
            return BufferLocally(batches, 0);

        var database = connection.GetDatabase();
        var stopwatch = Stopwatch.StartNew();
        var completedBatches = 0;
        try
        {
            await EnsureConsumerGroupAsync(database);
            for (; completedBatches < batches.Count; completedBatches++)
                await AppendBatchAsync(database, batches[completedBatches]);

            runtimeState.RecordSuccess("stream");
            telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Success, stopwatch.Elapsed);
            return new TeamLabTrafficEnqueueResult(envelopes.Count, batches.Count, 0, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("stream", "stream-append-failed");
            telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Failure, stopwatch.Elapsed);
            logger.LogWarning(exception,
                "TeamLab traffic stream append failed; buffering {Count} samples locally",
                batches.Skip(completedBatches).Sum(batch => batch.Count));
            return BufferLocally(batches, completedBatches);
        }
    }

    public async ValueTask<TeamLabTrafficReadBatch> ReadAsync(
        string consumerName,
        int maxCount,
        TimeSpan reclaimIdle,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        var take = Math.Clamp(maxCount, 1, TeamLabTrafficIngestionLimits.MaxBatchSamples);
        var connection = await TryGetConnectionAsync(cancellationToken);
        if (connection is null)
            return ReadLocal(take);

        var database = connection.GetDatabase();
        var messages = new List<TeamLabTrafficIngestMessage>(take);
        var malformedIds = new List<RedisValue>();
        try
        {
            await EnsureConsumerGroupAsync(database);
            var reclaimCount = Math.Max(1, take / 4);
            var reclaimCursor = _reclaimCursors.GetOrAdd(consumerName, "0-0");
            var reclaimed = await database.StreamAutoClaimAsync(
                _streamKey,
                ConsumerGroup,
                consumerName,
                Math.Max(1, (long)reclaimIdle.TotalMilliseconds),
                reclaimCursor,
                reclaimCount);
            _reclaimCursors[consumerName] = reclaimed.NextStartId;
            AddEntries(reclaimed.ClaimedEntries, messages, malformedIds);

            var remaining = take - messages.Count;
            if (remaining > 0)
            {
                var fresh = await database.StreamReadGroupAsync(
                    _streamKey, ConsumerGroup, consumerName, ">", remaining, noAck: false);
                AddEntries(fresh, messages, malformedIds);
            }

            if (malformedIds.Count > 0)
            {
                await database.StreamAcknowledgeAsync(_streamKey, ConsumerGroup, malformedIds.ToArray());
                logger.LogError("Discarded {Count} malformed TeamLab traffic stream entries", malformedIds.Count);
            }

            runtimeState.RecordSuccess("stream");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("stream", "stream-read-failed");
            logger.LogWarning(exception, "TeamLab traffic stream read failed; consuming local fallback only");
        }

        if (messages.Count < take)
            messages.AddRange(localBuffer.Drain(take - messages.Count)
                .Select(item => new TeamLabTrafficIngestMessage(null, item)));

        if (messages.Count == 0)
            return TeamLabTrafficReadBatch.Empty;

        var oldest = messages.Min(item => item.Envelope.CapturedAt);
        runtimeState.SetStreamConsumerLag(DateTimeOffset.UtcNow - oldest);
        return new TeamLabTrafficReadBatch(messages);
    }

    public async ValueTask AcknowledgeAsync(
        IReadOnlyCollection<string> streamIds,
        CancellationToken cancellationToken)
    {
        if (streamIds.Count == 0)
            return;

        var connection = await connections.GetAsync(cancellationToken);
        if (connection is null)
            throw new InvalidOperationException("Redis became unavailable before TeamLab traffic acknowledgement.");

        await connection.GetDatabase().StreamAcknowledgeAsync(
            _streamKey,
            ConsumerGroup,
            streamIds.Select(id => (RedisValue)id).ToArray());
    }

    internal static IReadOnlyList<IReadOnlyList<TeamLabTrafficEnvelope>> CreateBatches(
        IReadOnlyCollection<TeamLabTrafficEnvelope> envelopes)
    {
        var batches = new List<IReadOnlyList<TeamLabTrafficEnvelope>>();
        var current = new List<TeamLabTrafficEnvelope>(Math.Min(
            envelopes.Count, TeamLabTrafficIngestionLimits.MaxBatchSamples));
        var currentBytes = 0;

        foreach (var envelope in envelopes)
        {
            envelope.Validate();
            var bytes = envelope.GetSerializedSize();
            if (bytes > TeamLabTrafficIngestionLimits.MaxBatchBytes)
                throw new ArgumentException("A TeamLab traffic envelope exceeds the 1 MiB ingest limit.");

            if (current.Count == TeamLabTrafficIngestionLimits.MaxBatchSamples ||
                current.Count > 0 && currentBytes + bytes > TeamLabTrafficIngestionLimits.MaxBatchBytes)
            {
                batches.Add(current.ToArray());
                current = new List<TeamLabTrafficEnvelope>();
                currentBytes = 0;
            }

            current.Add(envelope);
            currentBytes += bytes;
        }

        if (current.Count > 0)
            batches.Add(current.ToArray());
        return batches;
    }

    private async Task AppendBatchAsync(IDatabase database, IReadOnlyList<TeamLabTrafficEnvelope> batch)
    {
        var redisBatch = database.CreateBatch();
        var writes = batch.Select(envelope => redisBatch.StreamAddAsync(_streamKey, ToEntries(envelope))).ToArray();
        redisBatch.Execute();
        await Task.WhenAll(writes);
        await database.ScriptEvaluateAsync(ProtectedTrimScript, new
        {
            stream = _streamKey,
            group = (RedisValue)ConsumerGroup,
            maxLength = MaxStreamLength
        });
    }

    private async Task EnsureConsumerGroupAsync(IDatabase database)
    {
        if (Volatile.Read(ref _groupReady) != 0)
            return;

        await _groupGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _groupReady) != 0)
                return;
            try
            {
                await database.StreamCreateConsumerGroupAsync(
                    _streamKey, ConsumerGroup, "0-0", createStream: true);
            }
            catch (RedisServerException exception) when (
                exception.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
            {
            }

            Volatile.Write(ref _groupReady, 1);
        }
        finally
        {
            _groupGate.Release();
        }
    }

    private async ValueTask<IConnectionMultiplexer?> TryGetConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await connections.GetAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("stream", "connection-unavailable");
            logger.LogWarning(exception, "Redis is unavailable for TeamLab traffic ingestion");
            return null;
        }
    }

    private TeamLabTrafficEnqueueResult BufferLocally(
        IReadOnlyList<IReadOnlyList<TeamLabTrafficEnvelope>> batches,
        int firstBatch)
    {
        var pending = batches.Skip(firstBatch).SelectMany(batch => batch).ToArray();
        var dropped = localBuffer.EnqueueRange(pending);
        telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Bypassed);
        return new TeamLabTrafficEnqueueResult(pending.Length, batches.Count - firstBatch, dropped, true);
    }

    private TeamLabTrafficReadBatch ReadLocal(int take)
    {
        var messages = localBuffer.Drain(take)
            .Select(item => new TeamLabTrafficIngestMessage(null, item))
            .ToArray();
        return messages.Length == 0 ? TeamLabTrafficReadBatch.Empty : new TeamLabTrafficReadBatch(messages);
    }

    private static void AddEntries(
        IEnumerable<StreamEntry> entries,
        ICollection<TeamLabTrafficIngestMessage> messages,
        ICollection<RedisValue> malformedIds)
    {
        foreach (var entry in entries)
        {
            try
            {
                messages.Add(new TeamLabTrafficIngestMessage(entry.Id.ToString(), FromEntry(entry)));
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
            {
                malformedIds.Add(entry.Id);
            }
        }
    }

    private static NameValueEntry[] ToEntries(TeamLabTrafficEnvelope envelope) =>
    [
        new("schemaVersion", envelope.SchemaVersion),
        new("runtimeId", envelope.RuntimeId),
        new("generation", envelope.Generation),
        new("shardId", envelope.ShardId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("networkId", envelope.NetworkId),
        new("workerNodeId", envelope.WorkerNodeId?.ToString("D") ?? string.Empty),
        new("capturedAt", envelope.CapturedAt.ToUnixTimeMilliseconds()),
        new("sourceCursor", envelope.SourceCursor),
        new("fingerprint", envelope.Fingerprint),
        new("sourceIp", envelope.SourceIp),
        new("sourcePort", envelope.SourcePort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("destinationIp", envelope.DestinationIp),
        new("destinationPort", envelope.DestinationPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("protocol", envelope.Protocol),
        new("packets", envelope.Packets),
        new("bytes", envelope.Bytes)
    ];

    private static TeamLabTrafficEnvelope FromEntry(StreamEntry entry)
    {
        var values = entry.Values.ToDictionary(item => item.Name.ToString(), item => item.Value.ToString(),
            StringComparer.Ordinal);
        string Required(string name) => values.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new FormatException($"Missing TeamLab stream field '{name}'.");
        int RequiredInt(string name) => int.Parse(Required(name), CultureInfo.InvariantCulture);
        long RequiredLong(string name) => long.Parse(Required(name), CultureInfo.InvariantCulture);
        int? NullableInt(string name) => values.GetValueOrDefault(name) is { Length: > 0 } value
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : null;
        Guid? NullableGuid(string name) => values.GetValueOrDefault(name) is { Length: > 0 } value
            ? Guid.Parse(value)
            : null;

        var envelope = new TeamLabTrafficEnvelope(
            RequiredInt("schemaVersion"),
            RequiredInt("runtimeId"),
            RequiredInt("generation"),
            NullableInt("shardId"),
            RequiredInt("networkId"),
            NullableGuid("workerNodeId"),
            DateTimeOffset.FromUnixTimeMilliseconds(RequiredLong("capturedAt")),
            RequiredLong("sourceCursor"),
            Required("fingerprint"),
            Required("sourceIp"),
            NullableInt("sourcePort"),
            Required("destinationIp"),
            NullableInt("destinationPort"),
            Required("protocol"),
            RequiredLong("packets"),
            RequiredLong("bytes"));
        envelope.Validate();
        return envelope;
    }
}
