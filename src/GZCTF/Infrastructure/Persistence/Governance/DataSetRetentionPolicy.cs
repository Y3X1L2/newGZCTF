namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed record DataSetRetentionPolicy(
    string Name,
    string OwnerModule,
    DataLifecycleMode Mode,
    TimeSpan? RawRetention,
    TimeSpan? AggregateRetention,
    PartitionGrain PartitionGrain,
    int DeleteBatchSize,
    string ArchiveAction,
    string FailureMode);

public enum DataLifecycleMode : byte
{
    OwnerManaged = 0,
    PartitionedRaw = 1,
    TerminalHistory = 2,
    Aggregate = 3
}

public enum PartitionGrain : byte
{
    None = 0,
    Day = 1,
    Month = 2
}
