namespace GZCTF.Modules.Runtime.Contracts;

public sealed record NodeLiveState(
    Guid WorkerNodeId,
    long Sequence,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    float CpuLoad,
    float MemoryLoad,
    int CurrentContainers,
    int CurrentVms,
    int UsedPorts)
{
    public bool IsFresh(DateTimeOffset utcNow, TimeSpan freshnessTtl) =>
        ReceivedAt <= utcNow && ReceivedAt >= utcNow - freshnessTtl;
}

public readonly record struct NodeLiveStateWriteResult(bool Accepted, bool UsedFallback)
{
    public static NodeLiveStateWriteResult Stored => new(true, false);
    public static NodeLiveStateWriteResult Buffered => new(true, true);
    public static NodeLiveStateWriteResult Rejected => new(false, false);
}

public sealed record NodeMetricStreamEntry(string EntryId, NodeLiveState State);
