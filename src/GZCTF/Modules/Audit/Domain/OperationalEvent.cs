namespace GZCTF.Modules.Audit.Domain;

public sealed class OperationalEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public OperationalEventSeverity Severity { get; set; } = OperationalEventSeverity.Information;
    public OperationalEventOutcome Outcome { get; set; } = OperationalEventOutcome.Observed;
    public OperationalErrorCategory? ErrorCategory { get; set; }
    public string? ErrorCode { get; set; }
    public bool Retryable { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DetailJson { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int? OwnerTeamId { get; set; }
    public int? GameId { get; set; }
    public int? CourseId { get; set; }
    public int? ChallengeId { get; set; }
    public int? ImageTemplateId { get; set; }
    public Guid? WorkerNodeId { get; set; }
    public Guid? DeploymentTicketId { get; set; }
    public int? TeamLabRuntimeId { get; set; }
    public Guid? VmInstanceId { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectId { get; set; }
    public string? SubjectDisplayName { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? ResourceDisplayName { get; set; }
}

public enum OperationalEventSeverity : byte
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public enum OperationalEventOutcome : byte
{
    Started = 0,
    Pending = 1,
    Blocked = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Recovered = 6,
    Observed = 7
}

public enum OperationalErrorCategory : byte
{
    Authorization = 0,
    Validation = 1,
    Conflict = 2,
    Scheduling = 3,
    Capacity = 4,
    ImageRegistry = 5,
    ImageTransfer = 6,
    NodeUnavailable = 7,
    AgentProtocol = 8,
    AgentTransport = 9,
    Docker = 10,
    Kvm = 11,
    Network = 12,
    HealthCheck = 13,
    Storage = 14,
    Database = 15,
    Cache = 16,
    Unknown = 17
}
