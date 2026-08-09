namespace GZCTF.Agent.Services.Observation;

public sealed record FlowAccumulatorSnapshot(int ActiveCount, long EvictedCount);

public sealed class FlowAccumulator
{
    private readonly int _capacity;
    private readonly Dictionary<string, FlowState> _flows = new(StringComparer.Ordinal);
    private PriorityQueue<string, FlowPriority> _eviction = new();
    private long _evicted;

    public FlowAccumulator(int capacity)
    {
        _capacity = Math.Clamp(capacity, 128, 1_000_000);
    }

    public void Observe(string flowFingerprint, long sequence, DateTimeOffset observedAt, int packetLength)
    {
        lock (_flows)
        {
            if (_flows.TryGetValue(flowFingerprint, out var current))
            {
                _flows[flowFingerprint] = current with
                {
                    LastSequence = sequence,
                    LastSeenAt = observedAt,
                    Packets = current.Packets + 1,
                    Bytes = current.Bytes + packetLength
                };
                _eviction.Enqueue(flowFingerprint, new FlowPriority(sequence, flowFingerprint));
                CompactQueueIfNeeded();
                return;
            }
            if (_flows.Count >= _capacity)
            {
                var removed = false;
                while (_eviction.TryDequeue(out var victim, out var priority))
                {
                    if (!_flows.TryGetValue(victim, out var state) || state.LastSequence != priority.Sequence)
                        continue;
                    _flows.Remove(victim);
                    _evicted++;
                    removed = true;
                    break;
                }
                if (!removed)
                {
                    var victim = _flows.Aggregate((left, right) =>
                        left.Value.LastSequence < right.Value.LastSequence ||
                        left.Value.LastSequence == right.Value.LastSequence &&
                        StringComparer.Ordinal.Compare(left.Key, right.Key) < 0
                            ? left
                            : right).Key;
                    _flows.Remove(victim);
                    _evicted++;
                }
            }
            _flows[flowFingerprint] = new FlowState(sequence, observedAt, observedAt, 1, packetLength);
            _eviction.Enqueue(flowFingerprint, new FlowPriority(sequence, flowFingerprint));
            CompactQueueIfNeeded();
        }
    }

    public FlowAccumulatorSnapshot Snapshot()
    {
        lock (_flows) return new FlowAccumulatorSnapshot(_flows.Count, _evicted);
    }

    private void CompactQueueIfNeeded()
    {
        if (_eviction.Count <= _capacity * 4) return;
        var compacted = new PriorityQueue<string, FlowPriority>(_flows.Count);
        foreach (var (key, state) in _flows)
            compacted.Enqueue(key, new FlowPriority(state.LastSequence, key));
        _eviction = compacted;
    }

    private readonly record struct FlowPriority(long Sequence, string Key) : IComparable<FlowPriority>
    {
        public int CompareTo(FlowPriority other)
        {
            var sequence = Sequence.CompareTo(other.Sequence);
            return sequence != 0 ? sequence : StringComparer.Ordinal.Compare(Key, other.Key);
        }
    }

    private sealed record FlowState(
        long LastSequence,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        long Packets,
        long Bytes);
}
