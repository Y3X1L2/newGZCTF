using System.Globalization;
using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using StackExchange.Redis;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class RedisNodeLiveStateStore : INodeLiveStateStore, INodeMetricStreamSource
{
    private const string ConsumerGroup = "node-metric-persistence";
    private const int MaximumStreamLength = 100_000;
    private const string WriteScript = """
        local current = redis.call('HGET', KEYS[1], 'sequence')
        if current and tonumber(current) >= tonumber(ARGV[2]) then
            return 0
        end

        redis.call('HSET', KEYS[1],
            'sequence', ARGV[2],
            'observedAt', ARGV[3],
            'receivedAt', ARGV[4],
            'cpuLoad', ARGV[5],
            'memoryLoad', ARGV[6],
            'currentContainers', ARGV[7],
            'currentVms', ARGV[8],
            'usedPorts', ARGV[9])
        redis.call('PEXPIRE', KEYS[1], ARGV[10])
        redis.call('XADD', KEYS[2], 'MAXLEN', '~', ARGV[11], '*',
            'workerNodeId', ARGV[1],
            'sequence', ARGV[2],
            'observedAt', ARGV[3],
            'receivedAt', ARGV[4],
            'cpuLoad', ARGV[5],
            'memoryLoad', ARGV[6],
            'currentContainers', ARGV[7],
            'currentVms', ARGV[8],
            'usedPorts', ARGV[9])
        return 1
        """;

    private readonly IRedisConnectionProvider _connectionProvider;
    private readonly RedisKeyspace _keyspace;
    private readonly PostgresNodeLiveStateFallback _fallback;
    private readonly RedisRuntimeState _runtimeState;
    private readonly ILogger<RedisNodeLiveStateStore> _logger;
    private readonly RedisKey _streamKey;
    private readonly RedisValue _consumerName;
    private readonly SemaphoreSlim _groupInitialization = new(1, 1);
    private int _groupReady;

    public RedisNodeLiveStateStore(
        IRedisConnectionProvider connectionProvider,
        RedisKeyspace keyspace,
        PostgresNodeLiveStateFallback fallback,
        RedisRuntimeState runtimeState,
        ILogger<RedisNodeLiveStateStore> logger)
    {
        _connectionProvider = connectionProvider;
        _keyspace = keyspace;
        _fallback = fallback;
        _runtimeState = runtimeState;
        _logger = logger;
        _streamKey = _keyspace.Create(RedisKeyPurpose.Stream, "node-metrics");
        _consumerName = $"{Environment.MachineName.ToLowerInvariant()}-{Environment.ProcessId}-{Guid.NewGuid():N}";
    }

    public TimeSpan FreshnessTtl { get; } = TimeSpan.FromSeconds(120);

    public async ValueTask<NodeLiveStateWriteResult> WriteAsync(NodeLiveState state,
        CancellationToken cancellationToken = default)
    {
        Validate(state);
        try
        {
            var connection = await _connectionProvider.GetAsync(cancellationToken);
            if (connection is null)
                return _fallback.Buffer(state);

            var database = connection.GetDatabase();
            var result = await database.ScriptEvaluateAsync(
                    WriteScript,
                    [LatestKey(state.WorkerNodeId), _streamKey],
                    [
                        state.WorkerNodeId.ToString("N"),
                        state.Sequence,
                        state.ObservedAt.ToUnixTimeMilliseconds(),
                        state.ReceivedAt.ToUnixTimeMilliseconds(),
                        state.CpuLoad.ToString("R", CultureInfo.InvariantCulture),
                        state.MemoryLoad.ToString("R", CultureInfo.InvariantCulture),
                        state.CurrentContainers,
                        state.CurrentVms,
                        state.UsedPorts,
                        checked((long)FreshnessTtl.TotalMilliseconds),
                        MaximumStreamLength
                    ])
                .WaitAsync(cancellationToken);

            _runtimeState.RecordSuccess("stream");
            return (long)result == 1
                ? NodeLiveStateWriteResult.Stored
                : NodeLiveStateWriteResult.Rejected;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or InvalidOperationException)
        {
            _runtimeState.RecordFailure("stream", "node-live-write-failed");
            _logger.LogWarning(exception,
                "Redis node live-state write failed; buffering node metric for PostgreSQL persistence");
            return _fallback.Buffer(state);
        }
    }

    public async ValueTask<NodeLiveState?> GetAsync(Guid workerNodeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connectionProvider.GetAsync(cancellationToken);
            if (connection is not null)
            {
                var entries = await connection.GetDatabase().HashGetAllAsync(LatestKey(workerNodeId))
                    .WaitAsync(cancellationToken);
                var state = ParseHash(workerNodeId, entries);
                if (state is not null)
                {
                    _runtimeState.RecordSuccess("stream");
                    return state;
                }
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or InvalidOperationException)
        {
            _runtimeState.RecordFailure("stream", "node-live-read-failed");
            _logger.LogDebug(exception, "Redis node live-state read failed; reading PostgreSQL checkpoint");
        }

        return await _fallback.GetAsync(workerNodeId, cancellationToken);
    }

    public async ValueTask<IReadOnlyDictionary<Guid, NodeLiveState>> GetManyAsync(
        IReadOnlyCollection<Guid> workerNodeIds,
        CancellationToken cancellationToken = default)
    {
        if (workerNodeIds.Count == 0)
            return new Dictionary<Guid, NodeLiveState>();

        try
        {
            var connection = await _connectionProvider.GetAsync(cancellationToken);
            if (connection is not null)
            {
                var database = connection.GetDatabase();
                var requested = workerNodeIds.Distinct().ToArray();
                var reads = requested.Select(async workerNodeId =>
                {
                    var entries = await database.HashGetAllAsync(LatestKey(workerNodeId));
                    return ParseHash(workerNodeId, entries);
                }).ToArray();
                var states = await Task.WhenAll(reads).WaitAsync(cancellationToken);
                var found = states.OfType<NodeLiveState>()
                    .ToDictionary(state => state.WorkerNodeId);

                if (found.Count == requested.Length)
                {
                    _runtimeState.RecordSuccess("stream");
                    return found;
                }

                var missing = requested.Where(workerNodeId => !found.ContainsKey(workerNodeId)).ToArray();
                var fallback = await _fallback.GetManyAsync(missing, cancellationToken);
                foreach (var item in fallback)
                    found.TryAdd(item.Key, item.Value);
                return found;
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or InvalidOperationException)
        {
            _runtimeState.RecordFailure("stream", "node-live-read-many-failed");
            _logger.LogDebug(exception, "Redis node live-state batch read failed; reading PostgreSQL checkpoints");
        }

        return await _fallback.GetManyAsync(workerNodeIds, cancellationToken);
    }

    public async Task<IReadOnlyList<NodeMetricStreamEntry>> ReadBatchAsync(int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        try
        {
            var connection = await _connectionProvider.GetAsync(cancellationToken);
            if (connection is null)
                return [];

            var database = connection.GetDatabase();
            await EnsureConsumerGroupAsync(database, cancellationToken);
            var entries = await database.StreamReadGroupAsync(
                    _streamKey, ConsumerGroup, _consumerName, StreamPosition.NewMessages, maximumCount)
                .WaitAsync(cancellationToken);
            if (entries.Length < maximumCount)
            {
                var reclaimed = await database.StreamAutoClaimAsync(
                        _streamKey,
                        ConsumerGroup,
                        _consumerName,
                        checked((long)FreshnessTtl.TotalMilliseconds),
                        "0-0",
                        maximumCount - entries.Length)
                    .WaitAsync(cancellationToken);
                entries = entries.Concat(reclaimed.ClaimedEntries).ToArray();
            }
            var result = entries.Select(ParseStreamEntry).Where(entry => entry is not null)
                .Select(entry => entry!).ToArray();
            _runtimeState.RecordSuccess("stream");
            return result;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or InvalidOperationException)
        {
            _runtimeState.RecordFailure("stream", "node-metric-consume-failed");
            _logger.LogWarning(exception, "Redis node metric stream read failed");
            return [];
        }
    }

    public async Task AcknowledgeAsync(IReadOnlyCollection<string> entryIds,
        CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
            return;

        var connection = await _connectionProvider.GetAsync(cancellationToken);
        if (connection is null)
            return;

        await connection.GetDatabase().StreamAcknowledgeAsync(
                _streamKey, ConsumerGroup, entryIds.Select(id => (RedisValue)id).ToArray())
            .WaitAsync(cancellationToken);
    }

    private RedisKey LatestKey(Guid workerNodeId) =>
        _keyspace.CreateTagged(RedisKeyPurpose.Stream, "node-live", workerNodeId.ToString("N"), "latest");

    private async Task EnsureConsumerGroupAsync(IDatabase database, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _groupReady) != 0)
            return;

        await _groupInitialization.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _groupReady) != 0)
                return;

            try
            {
                await database.StreamCreateConsumerGroupAsync(
                        _streamKey, ConsumerGroup, StreamPosition.Beginning, createStream: true)
                    .WaitAsync(cancellationToken);
            }
            catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP",
                       StringComparison.OrdinalIgnoreCase))
            {
            }

            Volatile.Write(ref _groupReady, 1);
        }
        finally
        {
            _groupInitialization.Release();
        }
    }

    private static NodeLiveState? ParseHash(Guid workerNodeId, HashEntry[] entries)
    {
        if (entries.Length == 0)
            return null;

        var values = entries.ToDictionary(entry => entry.Name.ToString(), entry => entry.Value.ToString(),
            StringComparer.Ordinal);
        return TryParseState(workerNodeId, values, out var state) ? state : null;
    }

    private static NodeMetricStreamEntry? ParseStreamEntry(StreamEntry entry)
    {
        var values = entry.Values.ToDictionary(item => item.Name.ToString(), item => item.Value.ToString(),
            StringComparer.Ordinal);
        if (!Guid.TryParseExact(Get(values, "workerNodeId"), "N", out var workerNodeId) ||
            !TryParseState(workerNodeId, values, out var state))
            return null;

        return new(entry.Id.ToString(), state);
    }

    private static bool TryParseState(Guid workerNodeId, IReadOnlyDictionary<string, string> values,
        out NodeLiveState state)
    {
        state = default!;
        if (!long.TryParse(Get(values, "sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var sequence) ||
            !long.TryParse(Get(values, "observedAt"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var observedAt) ||
            !long.TryParse(Get(values, "receivedAt"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var receivedAt) ||
            !float.TryParse(Get(values, "cpuLoad"), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var cpuLoad) ||
            !float.TryParse(Get(values, "memoryLoad"), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var memoryLoad) ||
            !int.TryParse(Get(values, "currentContainers"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var currentContainers) ||
            !int.TryParse(Get(values, "currentVms"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var currentVms) ||
            !int.TryParse(Get(values, "usedPorts"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var usedPorts))
            return false;

        state = new(
            workerNodeId,
            sequence,
            DateTimeOffset.FromUnixTimeMilliseconds(observedAt),
            DateTimeOffset.FromUnixTimeMilliseconds(receivedAt),
            cpuLoad,
            memoryLoad,
            currentContainers,
            currentVms,
            usedPorts);
        return true;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static void Validate(NodeLiveState state)
    {
        if (state.WorkerNodeId == Guid.Empty || state.Sequence <= 0 ||
            !float.IsFinite(state.CpuLoad) || state.CpuLoad is < 0 or > 1 ||
            !float.IsFinite(state.MemoryLoad) || state.MemoryLoad is < 0 or > 1 ||
            state.CurrentContainers < 0 || state.CurrentVms < 0 || state.UsedPorts < 0)
            throw new ArgumentOutOfRangeException(nameof(state), "Node live-state values are invalid.");
    }
}
