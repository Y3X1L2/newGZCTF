namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabRuntimeOperationKind : byte
{
    Create = 0,
    Reset = 1,
    Destroy = 2,
    TopologyCreate = 3,
    TopologyUpdate = 4,
    TopologyDelete = 5,
    TopologyPublish = 6,
    AccessGrantCreate = 7,
    AccessGrantRevoke = 8,
    CaptureStart = 9,
    CaptureStop = 10
}

public sealed class TeamLabRuntimeOperationJob
{
    public Guid OperationId { get; set; }
    public TeamLabRuntimeOperationKind Kind { get; set; }
    public int? RuntimeId { get; set; }
    public Guid? RuntimePublicId { get; set; }
    public string? ProtectedPayload { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
