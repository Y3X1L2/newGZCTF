using System.ComponentModel.DataAnnotations;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.TeamLab.Domain;

public sealed class TeamLabWebhookSubscription
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public Guid ControlScopeId { get; set; }
    [MaxLength(2048)] public string EndpointUrl { get; set; } = string.Empty;
    public string EventTypesJson { get; set; } = "[]";
    public string SigningSecretEncrypted { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public long DeliveryCursor { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? NextDeliveryAt { get; set; }
    public Guid? CreatedById { get; set; }
    public Guid? ApiOperationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public List<TeamLabWebhookDeliveryFailure> Failures { get; set; } = [];
    public TeamLabControlScope? ControlScope { get; set; }
}

public sealed class TeamLabWebhookDeliveryFailure
{
    [Key] public long Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public long EventId { get; set; }
    [MaxLength(256)] public string EventStage { get; set; } = string.Empty;
    [MaxLength(1024)] public string Error { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabWebhookSubscription Subscription { get; set; } = null!;
}
