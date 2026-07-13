using System.ComponentModel.DataAnnotations;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Contracts;

public sealed class OperationalEventQueryModel
{
    public string? Cursor { get; set; }
    [Range(1, 200)] public int Count { get; set; } = 50;
    public Guid? CorrelationId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Domain { get; set; }
    public string? EventCode { get; set; }
    public OperationalEventOutcome? Outcome { get; set; }
    public OperationalErrorCategory? ErrorCategory { get; set; }
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
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
}

public sealed record OperationalEventLabels(
    string? Actor,
    string? Owner,
    string? Team,
    string? Game,
    string? Course,
    string? Challenge,
    string? ImageTemplate,
    string? WorkerNode,
    string? DeploymentTicket,
    string? TeamLabRuntime,
    string? VmInstance,
    string? Subject,
    string? Resource);

public sealed record OperationalEventViewModel(
    OperationalEventModel Event,
    string Domain,
    OperationalEventLabels Labels);

public sealed record OperationalEventViewPageModel(
    IReadOnlyList<OperationalEventViewModel> Items,
    string? NextCursor);

public sealed record OperationalCorrelationSummaryModel(
    Guid CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    OperationalEventOutcome Outcome,
    OperationalErrorCategory? ErrorCategory,
    string? ErrorCode,
    int EventCount,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> WorkerNodes,
    string? Subject,
    string? Resource,
    OperationalEventViewPageModel Timeline);
