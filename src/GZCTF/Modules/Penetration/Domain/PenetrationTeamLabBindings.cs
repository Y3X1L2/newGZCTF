namespace GZCTF.Modules.Penetration.Domain;

public enum PenetrationResetStatus : byte
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

public enum PenetrationResetFailureClass : byte
{
    None = 0,
    Scenario = 1,
    Infrastructure = 2
}

public enum PenetrationRuntimeBindingStatus : byte
{
    Active = 0,
    Destroying = 1,
    Destroyed = 2
}

public sealed class PenetrationGameLabBinding
{
    public int GameId { get; set; }
    public int TopologyId { get; set; }
    public Guid? ActiveReleaseId { get; set; }
    public int MaxResetCount { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PenetrationTeamRuntimeBinding
{
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public int RuntimeId { get; set; }
    public PenetrationRuntimeBindingStatus Status { get; set; } = PenetrationRuntimeBindingStatus.Active;
    public Guid? DestroyOperationId { get; set; }
    public DateTimeOffset? DestroyedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
