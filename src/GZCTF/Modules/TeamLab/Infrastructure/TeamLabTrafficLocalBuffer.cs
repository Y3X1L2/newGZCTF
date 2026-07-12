using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.TeamLab.Application;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabTrafficLocalBuffer
{
    public const int DefaultCapacity = 10_000;

    private readonly Queue<TeamLabTrafficEnvelope> _queue;
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly RedisTelemetry _telemetry;
    private long _droppedCount;

    public TeamLabTrafficLocalBuffer(RedisTelemetry telemetry) : this(DefaultCapacity, telemetry)
    {
    }

    internal TeamLabTrafficLocalBuffer(int capacity, RedisTelemetry telemetry)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _queue = new Queue<TeamLabTrafficEnvelope>(Math.Min(capacity, 1024));
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
                    _queue.Dequeue();
                    dropped++;
                }

                _queue.Enqueue(envelope);
            }
        }

        if (dropped == 0)
            return 0;

        Interlocked.Add(ref _droppedCount, dropped);
        for (var index = 0; index < dropped; index++)
            _telemetry.RecordOperation(RedisTelemetryPurpose.Stream, RedisTelemetryStatus.Dropped);
        return dropped;
    }

    public IReadOnlyList<TeamLabTrafficEnvelope> Drain(int maxCount)
    {
        if (maxCount < 1)
            return [];

        lock (_gate)
        {
            var count = Math.Min(maxCount, _queue.Count);
            if (count == 0)
                return [];

            var result = new TeamLabTrafficEnvelope[count];
            for (var index = 0; index < count; index++)
                result[index] = _queue.Dequeue();
            return result;
        }
    }
}
