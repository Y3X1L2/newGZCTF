namespace GZCTF.Modules.Audit.Domain;

public enum ApiOperationStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed class ApiOperation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Kind { get; set; } = string.Empty;
    public ApiOperationStatus Status { get; set; } = ApiOperationStatus.Pending;
    public string Stage { get; set; } = "pending";
    public Guid? ActorUserId { get; set; }
    public Guid ApiTokenId { get; set; }
    public string RouteKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public Guid? DeploymentQueueTicketId { get; set; }
    public long CurrentProgress { get; set; }
    public long TotalProgress { get; set; }
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
