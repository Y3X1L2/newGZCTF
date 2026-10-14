using System.ComponentModel.DataAnnotations;
using System.Net;

namespace GZCTF.Models.Data;

public class LogModel
{
    public long Id { get; set; }

    [Required]
    public DateTimeOffset TimeUtc { get; set; }

    [Required]
    [MaxLength(Limits.MaxLogLevelLength)]
    public string Level { get; set; } = string.Empty;

    [Required]
    [MaxLength(Limits.MaxLoggerLength)]
    public string Logger { get; set; } = string.Empty;

    public TaskStatus? Status { get; set; }

    public IPAddress? RemoteIP { get; set; }

    [MaxLength(Limits.MaxUserNameLength)]
    public string? UserName { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }

    public Guid? CorrelationId { get; set; }

    [MaxLength(64)]
    public string? TraceId { get; set; }

    [MaxLength(128)]
    public string? EventCode { get; set; }

    [MaxLength(64)]
    public string? ErrorCategory { get; set; }

    [MaxLength(128)]
    public string? ErrorCode { get; set; }

    public Guid? WorkerNodeId { get; set; }

    [MaxLength(128)]
    public string? WorkerNodeName { get; set; }

    public Guid? DeploymentTicketId { get; set; }

    [MaxLength(64)]
    public string? ResourceType { get; set; }

    [MaxLength(128)]
    public string? ResourceId { get; set; }

    [MaxLength(256)]
    public string? ResourceDisplayName { get; set; }

    public Guid? ActorUserId { get; set; }

    [MaxLength(32)]
    public string? ActorKind { get; set; }

    [MaxLength(32)]
    public string? Outcome { get; set; }

    public bool? Retryable { get; set; }

    [MaxLength(8192)]
    public string? DetailJson { get; set; }
}
