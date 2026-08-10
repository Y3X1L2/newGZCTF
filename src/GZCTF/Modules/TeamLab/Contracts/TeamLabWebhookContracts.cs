using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record CreateTeamLabWebhookModel(
    [Required] Guid ControlScopeId,
    [Required, MaxLength(2048)] string EndpointUrl,
    IReadOnlyList<string> EventTypes,
    bool Enabled = true,
    long? FromEventId = null);

public sealed record TeamLabWebhookFailureModel(
    long Id,
    long EventId,
    string EventStage,
    string Error,
    DateTimeOffset OccurredAt);

public sealed record TeamLabWebhookModel(
    Guid Id,
    Guid ControlScopeId,
    string EndpointUrl,
    IReadOnlyList<string> EventTypes,
    bool Active,
    long DeliveryCursor,
    int ConsecutiveFailures,
    DateTimeOffset? NextDeliveryAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    IReadOnlyList<TeamLabWebhookFailureModel> RecentFailures);

public sealed record TeamLabWebhookCreationResultModel(
    TeamLabWebhookModel Webhook,
    string? SigningSecret);

public sealed record TeamLabWebhookPageModel(
    IReadOnlyList<TeamLabWebhookModel> Items,
    string? NextCursor);

public sealed record TeamLabWebhookReplayResult(int Delivered, int Failed);

/// <summary>Immutable event envelope delivered to webhook endpoints.</summary>
public sealed record TeamLabWebhookEventEnvelope(
    string Id,
    string Type,
    DateTimeOffset OccurredAt,
    Guid ScopeId,
    string ResourceType,
    Guid ResourceId,
    int ResourceVersion,
    Guid? OperationId,
    string Level,
    string Message,
    string? AssetKey,
    string Url);

/// <summary>Stable webhook delivery failure codes.</summary>
public static class TeamLabWebhookErrorCodes
{
    public const string EndpointInvalid = "webhook_endpoint_invalid";
    public const string EndpointUnreachable = "webhook_endpoint_unreachable";
    public const string SubscriptionNotFound = "webhook_subscription_not_found";
}
