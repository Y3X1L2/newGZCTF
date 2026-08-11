using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.TeamLab.Application;
using StackExchange.Redis;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class RedisTeamLabTrafficIngestor(
    IRedisConnectionProvider connections,
    RedisKeyspace keyspace,
    RedisRuntimeState runtimeState,
    RedisTelemetry telemetry,
    ILogger<RedisTeamLabTrafficIngestor> logger) : ITeamLabTrafficIngestor
{
    public const string ConsumerGroup = "gzctf-teamlab-flow-v1";
    public const int MaxStreamLength = 250_000;

    private const string AppendBatchScript = """
        local stream = KEYS[1]
        local group = ARGV[1]
        local maximum = tonumber(ARGV[2])
        local count = #ARGV - 2
        local pending = redis.call('XPENDING', stream, group)
        if pending[1] == 0 then
            redis.call('XTRIM', stream, 'MAXLEN', maximum - count)
        else
            redis.call('XTRIM', stream, 'MINID', pending[2])
        end
        if redis.call('XLEN', stream) + count > maximum then
            return -1
        end
        for index = 3, #ARGV do
            redis.call('XADD', stream, '*', 'payload', ARGV[index])
        end
        return count
        """;

    private static readonly JsonSerializerOptions StreamPayloadJson = new(JsonSerializerDefaults.Web);

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
            return Deferred();

        var database = connection.GetDatabase();
        var stopwatch = Stopwatch.StartNew();
        var completedBatches = 0;
        try
        {
            await EnsureConsumerGroupAsync(database);
            for (; completedBatches < batches.Count; completedBatches++)
                await AppendBatchAsync(database, batches[completedBatches], cancellationToken);

            runtimeState.RecordSuccess("stream");
            telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Success, stopwatch.Elapsed);
            return new TeamLabTrafficEnqueueResult(envelopes.Count, batches.Count, 0, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("stream", "stream-append-failed");
            telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Failure, stopwatch.Elapsed);
            logger.LogWarning(exception,
                "TeamLab 流量流追加失败，节点将保留 {Count} 条样本等待下次收集",
                batches.Skip(completedBatches).Sum(batch => batch.Count));
            return Deferred();
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
            return TeamLabTrafficReadBatch.Empty;

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
                logger.LogError("已丢弃 {Count} 条格式错误的 TeamLab 流量流条目", malformedIds.Count);
            }

            runtimeState.RecordSuccess("stream");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("stream", "stream-read-failed");
            logger.LogWarning(exception, "TeamLab 流量流读取失败，仅消费本地回退数据");
        }

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

    private async Task AppendBatchAsync(
        IDatabase database,
        IReadOnlyList<TeamLabTrafficEnvelope> batch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = new RedisValue[batch.Count + 2];
        arguments[0] = ConsumerGroup;
        arguments[1] = MaxStreamLength;
        for (var index = 0; index < batch.Count; index++)
            arguments[index + 2] = JsonSerializer.Serialize(batch[index], StreamPayloadJson);
        var appended = (long)await database.ScriptEvaluateAsync(
            AppendBatchScript, [_streamKey], arguments);
        if (appended != batch.Count)
            throw new TeamLabTrafficStreamCapacityException();
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
            logger.LogWarning(exception, "Redis 不可用，TeamLab 流量摄取失败");
            return null;
        }
    }

    private TeamLabTrafficEnqueueResult Deferred()
    {
        telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Bypassed);
        return new TeamLabTrafficEnqueueResult(0, 0, 0, true);
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
        new("networkId", envelope.NetworkId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("observationPointId", envelope.ObservationPointId),
        new("observationPointKind", (int)envelope.ObservationPointKind),
        new("assetId", envelope.AssetId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("workerNodeId", envelope.WorkerNodeId.ToString("D")),
        new("capturedAt", envelope.CapturedAt.ToUnixTimeMilliseconds()),
        new("sourceSequence", envelope.SourceSequence),
        new("evidenceFingerprint", envelope.EvidenceFingerprint),
        new("sourceIp", envelope.SourceIp),
        new("sourcePort", envelope.SourcePort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("destinationIp", envelope.DestinationIp),
        new("destinationPort", envelope.DestinationPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("protocol", envelope.Protocol),
        new("tcpFlags", envelope.TcpFlags?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("packetLength", envelope.PacketLength),
        new("packetFingerprint", envelope.PacketFingerprint ?? string.Empty),
        new("flowFingerprint", envelope.FlowFingerprint),
        new("processIdentityHash", envelope.ProcessIdentityHash ?? string.Empty),
        new("evidenceKind", envelope.EvidenceKind),
        new("direction", envelope.Direction),
        new("packets", envelope.Packets),
        new("bytes", envelope.Bytes),
        new("firstSeenAt", (envelope.FirstSeenAt ?? envelope.CapturedAt).ToUnixTimeMilliseconds()),
        new("lastSeenAt", (envelope.LastSeenAt ?? envelope.CapturedAt).ToUnixTimeMilliseconds())
    ];

    private static TeamLabTrafficEnvelope FromEntry(StreamEntry entry)
    {
        var values = entry.Values.ToDictionary(item => item.Name.ToString(), item => item.Value.ToString(),
            StringComparer.Ordinal);
        if (values.TryGetValue("payload", out var payload))
        {
            var decoded = JsonSerializer.Deserialize<TeamLabTrafficEnvelope>(payload, StreamPayloadJson)
                          ?? throw new FormatException("TeamLab stream payload is empty.");
            decoded.Validate();
            return decoded;
        }
        string Required(string name) => values.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new FormatException($"Missing TeamLab stream field '{name}'.");
        int RequiredInt(string name) => int.Parse(Required(name), CultureInfo.InvariantCulture);
        long RequiredLong(string name) => long.Parse(Required(name), CultureInfo.InvariantCulture);
        int? NullableInt(string name) => values.GetValueOrDefault(name) is { Length: > 0 } value
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : null;
        byte? NullableByte(string name) => values.GetValueOrDefault(name) is { Length: > 0 } value
            ? byte.Parse(value, CultureInfo.InvariantCulture)
            : null;
        var envelope = new TeamLabTrafficEnvelope(
            RequiredInt("schemaVersion"),
            RequiredInt("runtimeId"),
            RequiredInt("generation"),
            NullableInt("shardId"),
            NullableInt("networkId"),
            RequiredInt("observationPointId"),
            byte.Parse(Required("observationPointKind"), CultureInfo.InvariantCulture),
            NullableInt("assetId"),
            Guid.Parse(Required("workerNodeId")),
            DateTimeOffset.FromUnixTimeMilliseconds(RequiredLong("capturedAt")),
            RequiredLong("sourceSequence"),
            Required("evidenceFingerprint"),
            Required("sourceIp"),
            NullableInt("sourcePort"),
            Required("destinationIp"),
            NullableInt("destinationPort"),
            Required("protocol"),
            NullableByte("tcpFlags"),
            RequiredInt("packetLength"),
            values.GetValueOrDefault("packetFingerprint") is { Length: > 0 } packetFingerprint
                ? packetFingerprint
                : null,
            Required("flowFingerprint"),
            values.GetValueOrDefault("processIdentityHash") is { Length: > 0 } processIdentityHash
                ? processIdentityHash
                : null,
            Required("evidenceKind"),
            Required("direction"),
            RequiredLong("packets"),
            RequiredLong("bytes"),
            DateTimeOffset.FromUnixTimeMilliseconds(
                values.TryGetValue("firstSeenAt", out var firstSeenAt) && firstSeenAt.Length > 0
                    ? long.Parse(firstSeenAt, CultureInfo.InvariantCulture)
                    : RequiredLong("capturedAt")),
            DateTimeOffset.FromUnixTimeMilliseconds(
                values.TryGetValue("lastSeenAt", out var lastSeenAt) && lastSeenAt.Length > 0
                    ? long.Parse(lastSeenAt, CultureInfo.InvariantCulture)
                    : RequiredLong("capturedAt")));
        envelope.Validate();
        return envelope;
    }

    private sealed class TeamLabTrafficStreamCapacityException()
        : InvalidOperationException("TeamLab traffic stream reached its protected capacity.");
}
