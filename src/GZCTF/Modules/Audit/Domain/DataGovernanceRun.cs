namespace GZCTF.Modules.Audit.Domain;

public sealed class DataGovernanceRun
{
    public long Id { get; set; }
    public string DataSet { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public DataGovernanceRunStatus Status { get; set; } = DataGovernanceRunStatus.Running;
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTimeOffset Cutoff { get; set; }
    public long RowsRead { get; set; }
    public long RowsAggregated { get; set; }
    public long RowsDeleted { get; set; }
    public string? PartitionName { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum DataGovernanceRunStatus : byte
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3
}
