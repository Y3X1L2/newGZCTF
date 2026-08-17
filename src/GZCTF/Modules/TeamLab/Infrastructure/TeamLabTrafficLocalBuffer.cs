using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.TeamLab.Application;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabTrafficLocalBuffer
{
    public const int DefaultCapacity = 10_000;

    private readonly LinkedList<LocalEnvelope> _queue = [];
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly RedisTelemetry _telemetry;
    private long _droppedCount;
    private long _nextSequence;

    public TeamLabTrafficLocalBuffer(RedisTelemetry telemetry) : this(DefaultCapacity, telemetry)
    {
    }

    internal TeamLabTrafficLocalBuffer(int capacity, RedisTelemetry telemetry)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _telemetry = telemetry;
    }

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public int EnqueueRange(IEnumerable<TeamLabTrafficEnvelope> envelopes)
    {
        var dropped = 0;
        lock (_gate)
        {
            foreach (var envelope in envelopes)
            {
                if (_queue.Count == _capacity)
                {
                    _queue.RemoveFirst();
                    dropped++;
                }
                _queue.AddLast(new LocalEnvelope(++_nextSequence, envelope));
            }
        }

        if (dropped == 0)
            return 0;

        Interlocked.Add(ref _droppedCount, dropped);
        for (var index = 0; index < dropped; index++)
            _telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Dropped);
        return dropped;
    }

    public IReadOnlyList<LocalEnvelope> Read(int maxCount)
    {
        if (maxCount < 1)
            return [];

        lock (_gate)
        {
            var count = Math.Min(maxCount, _queue.Count);
            if (count == 0)
                return [];

            return _queue.Take(count).ToArray();
        }
    }

    public void Acknowledge(IReadOnlyCollection<long> sequences)
    {
        if (sequences.Count == 0) return;
        var acknowledged = sequences.ToHashSet();
        lock (_gate)
            while (_queue.First is { Value.Sequence: var sequence } && acknowledged.Contains(sequence))
                _queue.RemoveFirst();
    }

    public sealed record LocalEnvelope(long Sequence, TeamLabTrafficEnvelope Envelope);
}
