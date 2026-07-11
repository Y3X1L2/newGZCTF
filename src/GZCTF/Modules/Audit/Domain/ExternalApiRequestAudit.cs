namespace GZCTF.Modules.Audit.Domain;

public sealed class ExternalApiRequestAudit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string TraceId { get; set; } = string.Empty;
    public Guid? OperationId { get; set; }
    public Guid? ApiTokenId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Scopes { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string RouteKey { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public int StatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public long RequestBytes { get; set; }
    public long ResponseBytes { get; set; }
    public string? RemoteIp { get; set; }
    public bool? IdempotencyReused { get; set; }
    public long DurationMilliseconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
