using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Pure envelope and signature logic. The envelope is derived only from the
/// immutable TeamLabEvent row, so replayed or re-delivered events keep the same
/// event id and payload.
/// </summary>
public static class TeamLabWebhookDelivery
{
    public const int MaxReplayEvents = 2000;
    public const int MaxConsecutiveFailures = 10;

    /// <summary>Authoritative event stages recorded by the platform, usable as webhook event types.</summary>
    public static readonly IReadOnlySet<string> KnownEventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "deploy", "reset", "destroy", "ready", "pause", "resume",
        "cleanup", "fabric", "bootstrap", "network", "route", "probe",
        "infrastructure", "access", "remote-access",
        "capture", "capture-expiry", "capture-upload", "capture-download",
        "observation", "sensor-authentication", "operation"
    };

    /// <summary>Rejects unknown event types with a stable 422 so a subscription can never silently die.</summary>
    public static void ValidateEventTypes(IReadOnlyList<string>? eventTypes)
    {
        if (eventTypes is null) return;
        var unknown = eventTypes
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Where(item => !KnownEventTypes.Contains(item))
            .ToArray();
        if (unknown.Length > 0)
            throw new TeamLabApiContractException(
                "webhook_event_type_invalid",
                $"未知的 webhook 事件类型：{string.Join(", ", unknown)}。支持的事件类型见服务能力文档。",
                422);
    }

    public static TeamLabWebhookEventEnvelope BuildEnvelope(
        TeamLabEvent localEvent,
        Guid scopeId) => BuildEnvelope(localEvent, scopeId, null);

    public static TeamLabWebhookEventEnvelope BuildEnvelope(
        TeamLabEvent localEvent,
        TeamLabRuntime runtime,
        Guid scopeId) => BuildEnvelope(localEvent, scopeId, runtime);

    private static TeamLabWebhookEventEnvelope BuildEnvelope(
        TeamLabEvent localEvent,
        Guid scopeId,
        TeamLabRuntime? runtime) => new(
        Id: $"teamlab:{localEvent.Id}",
        Type: localEvent.Stage,
        OccurredAt: localEvent.CreatedAt,
        ScopeId: scopeId,
        ResourceType: localEvent.ResourceType ?? "teamlab-runtime",
        ResourceId: localEvent.ResourcePublicId ?? runtime?.PublicId ??
                    throw new InvalidOperationException("Webhook event resource is missing."),
        ResourceVersion: localEvent.ResourceVersion > 0 ? localEvent.ResourceVersion : localEvent.Generation,
        OperationId: localEvent.OperationId,
        Level: localEvent.Level.ToString().ToLowerInvariant(),
        Message: localEvent.Message,
        AssetKey: localEvent.ObjectType == "asset" ? localEvent.ObjectId : null,
        Url: localEvent.ResourceUrl ?? $"/api/open/v1/teamlab/runtimes/{runtime?.PublicId ?? localEvent.ResourcePublicId:D}");

    public static string SerializeEnvelope(TeamLabWebhookEventEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, JsonOptions);

    public static string ComputeSignature(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    public static bool IsSuccess(global::System.Net.HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and < 300;

    public static TimeSpan RetryDelay(int consecutiveFailures) =>
        TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Clamp(consecutiveFailures - 1, 0, 6))));

    public static bool MatchesEventType(IReadOnlyList<string> eventTypes, string eventType) =>
        eventTypes.Count == 0 || eventTypes.Contains(eventType, StringComparer.Ordinal);

    public static string[] ParseEventTypes(string? json) =>
        string.IsNullOrWhiteSpace(json) || json == "[]"
            ? []
            : Parse(json);

    public static string SerializeEventTypes(IReadOnlyList<string> eventTypes)
    {
        var normalized = eventTypes
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    private static string[] Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
