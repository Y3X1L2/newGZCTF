using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Observation;

public sealed class ObservationBatchSpool : BackgroundService
{
    private readonly AgentTeamLabConfig _config;
    private readonly ILogger<ObservationBatchSpool> _logger;
    private readonly string _root;
    private readonly Func<CancellationToken, Task>? _beforePersist;
    private readonly ConcurrentDictionary<RuntimeKey, RuntimeBuffer> _buffers = [];
    private readonly ConcurrentDictionary<RuntimeKey, byte> _removed = [];
    private readonly ConcurrentDictionary<RuntimeKey, long> _epochs = [];
    private readonly ConcurrentDictionary<RuntimeKey, SemaphoreSlim> _mutationGates = [];
    // Runtime buffers retain records until persistence succeeds. This bounded channel only
    // schedules the single writer; a full channel delays a write instead of dropping data.
    private readonly Channel<SpoolWork> _writes = Channel.CreateBounded<SpoolWork>(
        new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    public ObservationBatchSpool(
        IOptions<AgentTeamLabConfig> options,
        ILogger<ObservationBatchSpool> logger)
        : this(options, logger, "/var/lib/gzctf/observations", null)
    {
    }

    internal ObservationBatchSpool(
        IOptions<AgentTeamLabConfig> options,
        ILogger<ObservationBatchSpool> logger,
        string root,
        Func<CancellationToken, Task>? beforePersist)
    {
        _config = options.Value;
        _logger = logger;
        _root = root;
        _beforePersist = beforePersist;
    }

    public long AppendPacket(
        ObservationPointRegistration registration,
        DateTimeOffset capturedAt,
        ParsedObservationPacket packet)
    {
        var key = new RuntimeKey(registration.RuntimeId, registration.Generation);
        if (!TryGetActiveEpoch(key, out var epoch)) return 0;
        var buffer = Buffer(key);
        if (_config.ObservationPacketFingerprintEnabled)
        {
            var sequence = buffer.NextSequence();
            var record = new TeamLabObservationRecord(
                sequence,
                registration.PublicId,
                null,
                capturedAt,
                packet.SourceIp,
                packet.SourcePort,
                packet.DestinationIp,
                packet.DestinationPort,
                packet.Protocol,
                packet.TcpFlags,
                packet.PacketLength,
                packet.PacketFingerprint,
                packet.FlowFingerprint,
                TeamLabObservationEvidenceKind.Packet,
                FirstSeenAt: capturedAt,
                LastSeenAt: capturedAt,
                Bytes: packet.PacketLength);
            return Append(buffer, record, epoch) ? sequence : 0;
        }

        var observed = buffer.ObservePacket(registration, capturedAt, packet);
        foreach (var record in observed.SealedRecords)
            Append(buffer, record, epoch);
        return _removed.ContainsKey(key) || _epochs.GetOrAdd(key, 0) != epoch
            ? 0
            : observed.PacketOrdinal;
    }

    public long AppendEndpoint(
        int runtimeId,
        int generation,
        string assetKey,
        DateTimeOffset observedAt,
        string sourceIp,
        int? sourcePort,
        string destinationIp,
        int? destinationPort,
        string protocol,
        string flowFingerprint,
        string processIdentityHash,
        string direction)
    {
        var key = new RuntimeKey(runtimeId, generation);
        if (!TryGetActiveEpoch(key, out var epoch)) return 0;
        var buffer = Buffer(key);
        var sequence = buffer.NextSequence();
        var record = new TeamLabObservationRecord(
            sequence,
            null,
            assetKey,
            observedAt,
            sourceIp,
            sourcePort,
            destinationIp,
            destinationPort,
            protocol,
            null,
            0,
            null,
            flowFingerprint,
            TeamLabObservationEvidenceKind.EndpointProcess,
            processIdentityHash,
            direction);
        return Append(buffer, record, epoch) ? sequence : 0;
    }

    public TeamLabObservationBatchResponse Read(TeamLabObservationBatchRequest request, TeamLabObservationHealth health)
    {
        var key = new RuntimeKey(request.RuntimeId, request.Generation);
        if (!_buffers.TryGetValue(key, out var buffer))
            return new TeamLabObservationBatchResponse(
                true, "No observations are available yet.", request.AfterSequence, 0,
                0, [], health);
        FlushAggregates(key, buffer);
        var limit = Math.Clamp(request.Limit, 1, Math.Clamp(_config.ObservationBatchSize, 1, 2_000));
        var snapshot = buffer.Read(request.AfterSequence, request.ObservationPointId, limit);
        return new TeamLabObservationBatchResponse(
            true,
            $"Loaded {snapshot.Records.Length} observation record(s).",
            snapshot.NextSequence,
            snapshot.DroppedCount,
            buffer.PersistedThrough,
            snapshot.Records,
            health with
            {
                DroppedCount = health.DroppedCount + snapshot.DroppedCount,
                SpoolBytes = buffer.SpoolBytes
            });
    }

    public async Task AcknowledgeAsync(int runtimeId, int generation, long sequence,
        CancellationToken cancellationToken)
    {
        if (sequence <= 0) return;
        var key = new RuntimeKey(runtimeId, generation);
        if (!_buffers.TryGetValue(key, out var buffer)) return;

        var gate = MutationGate(key);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_removed.ContainsKey(key) || !buffer.Acknowledge(sequence)) return;
            await WriteAcknowledgementLockedAsync(key, buffer.AcknowledgedThrough, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Remove(int runtimeId, int generation)
    {
        var key = new RuntimeKey(runtimeId, generation);
        _removed[key] = 0;
        _epochs.AddOrUpdate(key, 1, static (_, current) => current + 1);
        var gate = MutationGate(key);
        gate.Wait();
        try
        {
            if (_buffers.TryRemove(key, out var buffer))
                buffer.Clear();
            DeleteGenerationDirectory(key);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Activate(int runtimeId, int generation) =>
        _removed.TryRemove(new RuntimeKey(runtimeId, generation), out _);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RestoreAsync(stoppingToken);
        var persistence = PersistWritesAsync(stoppingToken);
        var interval = TimeSpan.FromMilliseconds(
            Math.Clamp(_config.ObservationAggregationIntervalMilliseconds, 100, 60_000));
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                foreach (var (key, buffer) in _buffers)
                {
                    FlushAggregates(key, buffer);
                    QueuePersistence(key, _epochs.GetOrAdd(key, 0), buffer);
                }
        }
        finally
        {
            _writes.Writer.TryComplete();
            try
            {
                await persistence;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task PersistWritesAsync(CancellationToken stoppingToken)
    {
        while (await _writes.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_writes.Reader.TryRead(out var work))
                await PersistWorkAsync(work, stoppingToken);
        }
    }

    private void FlushAggregates(RuntimeKey key, RuntimeBuffer buffer)
    {
        if (!TryGetActiveEpoch(key, out var epoch)) return;
        foreach (var record in buffer.FlushAggregates())
            Append(buffer, record, epoch);
    }

    private bool Append(RuntimeBuffer buffer, TeamLabObservationRecord record, long epoch)
    {
        if (_removed.ContainsKey(buffer.Key) || _epochs.GetOrAdd(buffer.Key, 0) != epoch)
        {
            buffer.RecordDrop();
            return false;
        }
        if (!buffer.Append(record)) return false;
        if (_removed.ContainsKey(buffer.Key) || _epochs.GetOrAdd(buffer.Key, 0) != epoch)
            return false;
        QueuePersistence(buffer.Key, epoch, buffer);
        return true;
    }

    private void QueuePersistence(RuntimeKey key, long epoch, RuntimeBuffer buffer)
    {
        if (!buffer.TrySchedulePersistence()) return;
        _ = QueuePersistenceAsync(key, epoch, buffer);
    }

    private async Task QueuePersistenceAsync(RuntimeKey key, long epoch, RuntimeBuffer buffer)
    {
        try
        {
            await _writes.Writer.WriteAsync(new SpoolWork(key, epoch));
        }
        catch (ChannelClosedException)
        {
            buffer.CancelPersistenceSchedule();
        }
    }

    private async Task PersistWorkAsync(SpoolWork work, CancellationToken cancellationToken)
    {
        if (_removed.ContainsKey(work.Key) || _epochs.GetOrAdd(work.Key, 0) != work.Epoch ||
            !_buffers.TryGetValue(work.Key, out var buffer))
            return;
        try
        {
            await AppendPendingAsync(work.Key, work.Epoch, buffer, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception,
                "Failed to persist TeamLab observation spool for runtime {RuntimeId} generation {Generation}.",
                work.Key.RuntimeId, work.Key.Generation);
        }
        finally
        {
            if (buffer.CompletePersistencePass())
                QueuePersistence(work.Key, work.Epoch, buffer);
        }
    }

    private bool TryGetActiveEpoch(RuntimeKey key, out long epoch)
    {
        epoch = _epochs.GetOrAdd(key, 0);
        return !_removed.ContainsKey(key);
    }

    private RuntimeBuffer Buffer(RuntimeKey key) =>
        _buffers.GetOrAdd(key, value =>
            new RuntimeBuffer(
                value,
                Math.Clamp(Math.Max(
                    _config.ObservationMemoryRecordLimit,
                    (int)Math.Min(1_000_000, Math.Max(1, _config.ObservationSpoolMaxBytes / 256))),
                    1_000, 1_000_000),
                Math.Clamp(_config.ObservationMaxActiveFlows, 128, 1_000_000)));

    private async Task AppendPendingAsync(
        RuntimeKey key,
        long epoch,
        RuntimeBuffer buffer,
        CancellationToken cancellationToken)
    {
        var gate = MutationGate(key);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_removed.ContainsKey(key) || _epochs.GetOrAdd(key, 0) != epoch)
                return;
            if (_beforePersist is not null)
                await _beforePersist(cancellationToken);
            if (_removed.ContainsKey(key) || _epochs.GetOrAdd(key, 0) != epoch)
                return;

            if (!_buffers.TryGetValue(key, out var activeBuffer)) return;
            buffer = activeBuffer;
            var pending = buffer.PendingPersistence();
            if (pending.Length == 0) return;
            var path = SpoolPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            await using (var writer = new StreamWriter(stream))
            {
                foreach (var record in pending)
                    await writer.WriteLineAsync(JsonSerializer.Serialize(record).AsMemory(), cancellationToken);
            }
            buffer.MarkPersisted(pending[^1].Sequence);
            var length = new FileInfo(path).Length;
            buffer.SpoolBytes = length;
            if (length > Math.Max(1_048_576, _config.ObservationSpoolMaxBytes))
                await CompactLockedAsync(key, path, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task CompactLockedAsync(RuntimeKey key, string path, CancellationToken cancellationToken)
    {
        if (!_buffers.TryGetValue(key, out var buffer)) return;
        var maximumBytes = Math.Max(1_048_576, _config.ObservationSpoolMaxBytes);
        var retained = RetainWithinBudget(buffer.All(), maximumBytes);
        buffer.Retain(retained.Select(item => item.Record).ToArray());
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                foreach (var item in retained)
                    await stream.WriteAsync(item.Line, cancellationToken);
            }
            File.Move(temporary, path, true);
            buffer.SpoolBytes = new FileInfo(path).Length;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task WriteAcknowledgementLockedAsync(RuntimeKey key, long sequence,
        CancellationToken cancellationToken)
    {
        var path = AcknowledgementPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary,
                sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root)) return;
        foreach (var path in Directory.EnumerateFiles(_root, "records.jsonl", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParsePath(path, out var key)) continue;
            if (_removed.ContainsKey(key)) continue;
            var buffer = Buffer(key);
            var gate = MutationGate(key);
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (_removed.ContainsKey(key)) continue;
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(stream);
                buffer.RestoreAcknowledgement(await ReadAcknowledgementAsync(key, cancellationToken));
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    var record = JsonSerializer.Deserialize<TeamLabObservationRecord>(line);
                    if (record is not null) buffer.Restore(record);
                }
                buffer.MarkRestoredPersistence();
                buffer.SpoolBytes = new FileInfo(path).Length;
                if (buffer.SpoolBytes > Math.Max(1_048_576, _config.ObservationSpoolMaxBytes))
                    await CompactLockedAsync(key, path, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Failed to restore TeamLab observation spool {Path}.", path);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private string SpoolPath(RuntimeKey key) =>
        Path.Combine(_root, $"runtime-{key.RuntimeId}", $"generation-{key.Generation}", "records.jsonl");

    private string AcknowledgementPath(RuntimeKey key) =>
        Path.Combine(Path.GetDirectoryName(SpoolPath(key))!, "acknowledged.seq");

    private async Task<long> ReadAcknowledgementAsync(RuntimeKey key, CancellationToken cancellationToken)
    {
        var path = AcknowledgementPath(key);
        if (!File.Exists(path)) return 0;
        var value = await File.ReadAllTextAsync(path, cancellationToken);
        return long.TryParse(value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var sequence) && sequence >= 0
            ? sequence
            : throw new JsonException("Observation acknowledgement watermark is invalid.");
    }

    private SemaphoreSlim MutationGate(RuntimeKey key) =>
        _mutationGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));

    private void DeleteGenerationDirectory(RuntimeKey key)
    {
        var generationDirectory = Path.GetDirectoryName(SpoolPath(key))!;
        if (Directory.Exists(generationDirectory))
            Directory.Delete(generationDirectory, true);
        var runtimeDirectory = Path.GetDirectoryName(generationDirectory);
        if (runtimeDirectory is null || !Directory.Exists(runtimeDirectory) ||
            Directory.EnumerateFileSystemEntries(runtimeDirectory).Any())
            return;
        try
        {
            Directory.Delete(runtimeDirectory);
        }
        catch (IOException)
        {
            // Another generation may have been activated concurrently.
        }
    }

    private static bool TryParsePath(string path, out RuntimeKey key)
    {
        key = default;
        var generationDirectory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        var runtimeDirectory = generationDirectory.Parent;
        if (runtimeDirectory is null || !generationDirectory.Name.StartsWith("generation-", StringComparison.Ordinal) ||
            !runtimeDirectory.Name.StartsWith("runtime-", StringComparison.Ordinal) ||
            !int.TryParse(generationDirectory.Name[11..], out var generation) ||
            !int.TryParse(runtimeDirectory.Name[8..], out var runtimeId))
            return false;
        key = new RuntimeKey(runtimeId, generation);
        return true;
    }

    private readonly record struct RuntimeKey(int RuntimeId, int Generation);
    private readonly record struct SpoolWork(RuntimeKey Key, long Epoch);
    private sealed record SerializedObservation(TeamLabObservationRecord Record, byte[] Line);

    private static IReadOnlyList<SerializedObservation> RetainWithinBudget(
        IReadOnlyList<TeamLabObservationRecord> records,
        long maximumBytes)
    {
        var retained = new List<SerializedObservation>();
        long total = 0;
        for (var index = records.Count - 1; index >= 0; index--)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(records[index]);
            var line = new byte[payload.Length + 1];
            payload.CopyTo(line, 0);
            line[^1] = (byte)'\n';
            if (line.LongLength > maximumBytes)
                continue;
            if (total + line.LongLength > maximumBytes)
                break;
            retained.Add(new SerializedObservation(records[index], line));
            total += line.LongLength;
        }
        retained.Reverse();
        return retained;
    }

    private sealed class RuntimeBuffer(RuntimeKey key, int capacity, int flowCapacity)
    {
        private readonly LinkedList<TeamLabObservationRecord> _records = [];
        private readonly Dictionary<PacketAggregateKey, PacketAggregate> _packetAggregates = [];
        private long _nextSequence;
        private long _packetOrdinal;
        private long _dropped;
        private long _persistedThrough;
        private bool _persistenceScheduled;
        public RuntimeKey Key { get; } = key;
        public long SpoolBytes { get; set; }
        public long AcknowledgedThrough { get; private set; }

        public long PersistedThrough
        {
            get
            {
                lock (_records) return _persistedThrough;
            }
        }

        public long NextSequence()
        {
            lock (_records) return ++_nextSequence;
        }

        public PacketObservationResult ObservePacket(
            ObservationPointRegistration registration,
            DateTimeOffset capturedAt,
            ParsedObservationPacket packet)
        {
            lock (_records)
            {
                var ordinal = ++_packetOrdinal;
                var aggregateKey = new PacketAggregateKey(
                    registration.PublicId,
                    packet.SourceIp,
                    packet.SourcePort,
                    packet.DestinationIp,
                    packet.DestinationPort,
                    packet.Protocol);
                if (_packetAggregates.TryGetValue(aggregateKey, out var current))
                {
                    current.Observe(ordinal, capturedAt, packet);
                    return new PacketObservationResult(ordinal, []);
                }

                _packetAggregates[aggregateKey] = new PacketAggregate(
                    aggregateKey,
                    ordinal,
                    capturedAt,
                    packet);
                if (_packetAggregates.Count <= flowCapacity)
                    return new PacketObservationResult(ordinal, []);

                var victim = _packetAggregates.Values.MinBy(item => item.LastOrdinal)!;
                _packetAggregates.Remove(victim.Key);
                return new PacketObservationResult(ordinal, [CreateAggregateRecord(victim)]);
            }
        }

        public TeamLabObservationRecord[] FlushAggregates()
        {
            lock (_records)
            {
                if (_packetAggregates.Count == 0) return [];
                var records = _packetAggregates.Values
                    .OrderBy(item => item.LastOrdinal)
                    .Select(CreateAggregateRecord)
                    .ToArray();
                _packetAggregates.Clear();
                return records;
            }
        }

        private TeamLabObservationRecord CreateAggregateRecord(PacketAggregate aggregate) => new(
            ++_nextSequence,
            aggregate.Key.ObservationPointId,
            null,
            aggregate.LastSeenAt,
            aggregate.Key.SourceIp,
            aggregate.Key.SourcePort,
            aggregate.Key.DestinationIp,
            aggregate.Key.DestinationPort,
            aggregate.Key.Protocol,
            aggregate.TcpFlags,
            aggregate.LastPacketLength,
            null,
            aggregate.FlowFingerprint,
            TeamLabObservationEvidenceKind.Packet,
            FirstSeenAt: aggregate.FirstSeenAt,
            LastSeenAt: aggregate.LastSeenAt,
            Packets: aggregate.Packets,
            Bytes: aggregate.Bytes);

        public bool Append(TeamLabObservationRecord record)
        {
            lock (_records)
            {
                while (_records.Count >= capacity &&
                       _records.First is { Value.Sequence: var first } && first <= AcknowledgedThrough)
                    _records.RemoveFirst();
                if (_records.Count >= capacity)
                {
                    // Never evict an unacknowledged record merely to stay within the
                    // in-memory window. The durable spool is the backlog; this counter is
                    // reserved for an actual configured retention limit.
                    _dropped++;
                    return false;
                }
                _records.AddLast(record);
                return true;
            }
        }

        public void Restore(TeamLabObservationRecord record)
        {
            lock (_records)
            {
                _nextSequence = Math.Max(_nextSequence, record.Sequence);
                if (record.Sequence <= AcknowledgedThrough) return;
                while (_records.Count >= capacity &&
                       _records.First is { Value.Sequence: var first } && first <= AcknowledgedThrough)
                    _records.RemoveFirst();
                if (_records.Count >= capacity)
                {
                    _dropped++;
                    return;
                }
                _records.AddLast(record);
            }
        }

        public void RecordDrop()
        {
            lock (_records) _dropped++;
        }

        public bool Acknowledge(long sequence)
        {
            lock (_records)
            {
                if (sequence <= AcknowledgedThrough) return false;
                // Acknowledging a record that has not reached disk would make an Agent restart
                // permanently lose it. The caller will retry this monotonic acknowledgement
                // after the writer advances the durable watermark.
                if (sequence > _persistedThrough) return false;
                AcknowledgedThrough = Math.Min(sequence, _persistedThrough);
                while (_records.First is { Value.Sequence: var value } && value <= AcknowledgedThrough)
                    _records.RemoveFirst();
                return true;
            }
        }

        public RuntimeRead Read(long after, Guid? pointId, int limit)
        {
            lock (_records)
            {
                var records = _records
                    .Where(item => item.Sequence > after &&
                                   (pointId is null || item.ObservationPointId == pointId))
                    .Take(limit)
                    .ToArray();
                return new RuntimeRead(
                    records.Length == 0 ? after : records[^1].Sequence,
                    _dropped,
                    records);
            }
        }

        public TeamLabObservationRecord[] All()
        {
            lock (_records) return _records.ToArray();
        }

        public void Retain(IReadOnlyCollection<TeamLabObservationRecord> records)
        {
            lock (_records)
            {
                _dropped += Math.Max(0, _records.Count - records.Count);
                _records.Clear();
                foreach (var record in records) _records.AddLast(record);
                _persistedThrough = Math.Max(AcknowledgedThrough,
                    records.Count == 0 ? 0 : records.Max(item => item.Sequence));
            }
        }

        public void RestoreAcknowledgement(long sequence)
        {
            lock (_records)
            {
                AcknowledgedThrough = sequence;
                _nextSequence = Math.Max(_nextSequence, sequence);
                _persistedThrough = Math.Max(_persistedThrough, sequence);
            }
        }

        public void MarkRestoredPersistence()
        {
            lock (_records) _persistedThrough = Math.Max(_persistedThrough, _nextSequence);
        }

        public TeamLabObservationRecord[] PendingPersistence()
        {
            lock (_records) return _records.Where(item => item.Sequence > _persistedThrough).ToArray();
        }

        public void MarkPersisted(long sequence)
        {
            lock (_records)
            {
                if (sequence <= _persistedThrough) return;
                _persistedThrough = sequence;
            }
        }

        public bool TrySchedulePersistence()
        {
            lock (_records)
            {
                if (_persistenceScheduled || _records.All(item => item.Sequence <= _persistedThrough)) return false;
                _persistenceScheduled = true;
                return true;
            }
        }

        public void CancelPersistenceSchedule()
        {
            lock (_records) _persistenceScheduled = false;
        }

        public bool CompletePersistencePass()
        {
            lock (_records)
            {
                _persistenceScheduled = false;
                return _records.Any(item => item.Sequence > _persistedThrough);
            }
        }

        public void Clear()
        {
            lock (_records)
            {
                _persistenceScheduled = false;
                _records.Clear();
                _packetAggregates.Clear();
            }
        }

    }

    private readonly record struct PacketAggregateKey(
        Guid ObservationPointId,
        string SourceIp,
        int? SourcePort,
        string DestinationIp,
        int? DestinationPort,
        string Protocol);

    private sealed class PacketAggregate(
        PacketAggregateKey key,
        long ordinal,
        DateTimeOffset capturedAt,
        ParsedObservationPacket packet)
    {
        public PacketAggregateKey Key { get; } = key;
        public long LastOrdinal { get; private set; } = ordinal;
        public DateTimeOffset FirstSeenAt { get; private set; } = capturedAt;
        public DateTimeOffset LastSeenAt { get; private set; } = capturedAt;
        public long Packets { get; private set; } = 1;
        public long Bytes { get; private set; } = packet.PacketLength;
        public byte? TcpFlags { get; private set; } = packet.TcpFlags;
        public int LastPacketLength { get; private set; } = packet.PacketLength;
        public string FlowFingerprint { get; } = packet.FlowFingerprint;

        public void Observe(long nextOrdinal, DateTimeOffset capturedAt, ParsedObservationPacket packet)
        {
            LastOrdinal = nextOrdinal;
            FirstSeenAt = capturedAt < FirstSeenAt ? capturedAt : FirstSeenAt;
            LastSeenAt = capturedAt > LastSeenAt ? capturedAt : LastSeenAt;
            Packets++;
            Bytes += packet.PacketLength;
            LastPacketLength = packet.PacketLength;
            if (packet.TcpFlags is { } flags)
                TcpFlags = (byte)((TcpFlags ?? 0) | flags);
        }
    }

    private sealed record PacketObservationResult(
        long PacketOrdinal,
        TeamLabObservationRecord[] SealedRecords);

    private sealed record RuntimeRead(
        long NextSequence,
        long DroppedCount,
        TeamLabObservationRecord[] Records);
}
