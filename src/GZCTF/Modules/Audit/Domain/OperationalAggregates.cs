namespace GZCTF.Modules.Audit.Domain;

public sealed class OperationalLogAggregate
{
    public long Id { get; set; }
    public DateTimeOffset BucketStart { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Logger { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DeploymentLifecycleAggregate
{
    public long Id { get; set; }
    public DateTimeOffset BucketStart { get; set; }
    public byte Kind { get; set; }
    public byte Status { get; set; }
    public Guid WorkerNodeId { get; set; }
    public long Count { get; set; }
    public long DurationCount { get; set; }
    public long DurationTotalMilliseconds { get; set; }
    public long DurationMaxMilliseconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
