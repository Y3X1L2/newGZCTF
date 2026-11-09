namespace GZCTF.Modules.Runtime.Contracts;

public sealed record NodeMetricHistory(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int IntervalSeconds,
    NodeMetricHistoryPoint[] Points);

public sealed record NodeMetricHistoryPoint(
    DateTimeOffset Timestamp,
    double? CpuLoad,
    double? MemoryLoad,
    int NodeCount,
    long SampleCount);
