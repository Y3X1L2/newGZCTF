using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// Log information (Admin)
/// </summary>
public class LogMessageModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    [JsonPropertyName("name")]
    public string? UserName { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("ip")]
    public IPAddress? IP { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("status")]
    public TaskStatus? Status { get; set; }

    [JsonPropertyName("correlationId")]
    public Guid? CorrelationId { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [JsonPropertyName("eventCode")]
    public string? EventCode { get; set; }

    [JsonPropertyName("errorCategory")]
    public string? ErrorCategory { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("workerNodeId")]
    public Guid? WorkerNodeId { get; set; }

    [JsonPropertyName("workerNodeName")]
    public string? WorkerNodeName { get; set; }

    [JsonPropertyName("deploymentTicketId")]
    public Guid? DeploymentTicketId { get; set; }

    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; set; }

    [JsonPropertyName("resourceId")]
    public string? ResourceId { get; set; }

    [JsonPropertyName("resourceDisplayName")]
    public string? ResourceDisplayName { get; set; }

    public Guid? ActorUserId { get; set; }
    public string? ActorKind { get; set; }
    public string? ActorDisplayName { get; set; }
    public string? Logger { get; set; }
    public string? Outcome { get; set; }
    public bool? Retryable { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? Detail { get; set; }
    public string[]? CurrentDisplayNames { get; set; }

    public static LogMessageModel FromLogModel(LogModel logInfo) =>
        new()
        {
            Id = logInfo.Id,
            Time = logInfo.TimeUtc,
            Level = logInfo.Level,
            UserName = logInfo.UserName,
            IP = logInfo.RemoteIP,
            Msg = logInfo.Message,
            Status = logInfo.Status,
            CorrelationId = logInfo.CorrelationId,
            TraceId = logInfo.TraceId,
            EventCode = logInfo.EventCode,
            ErrorCategory = logInfo.ErrorCategory,
            ErrorCode = logInfo.ErrorCode,
            WorkerNodeId = logInfo.WorkerNodeId,
            WorkerNodeName = logInfo.WorkerNodeName,
            DeploymentTicketId = logInfo.DeploymentTicketId,
            ResourceType = logInfo.ResourceType,
            ResourceId = logInfo.ResourceId,
            ResourceDisplayName = logInfo.ResourceDisplayName,
            ActorUserId = logInfo.ActorUserId,
            ActorKind = logInfo.ActorKind ?? "Unknown",
            ActorDisplayName = logInfo.UserName == "Anonymous" && logInfo.ActorKind is not "User" and not "ApiToken"
                ? null : logInfo.UserName,
            Logger = logInfo.Logger,
            Outcome = logInfo.Outcome,
            Retryable = logInfo.Retryable,
            Detail = ParseDetail(logInfo.DetailJson)
        };

    private static IReadOnlyDictionary<string, JsonElement>? ParseDetail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record LogMessagePageModel(IReadOnlyList<LogMessageModel> Items, string? NextCursor);

public sealed class LogQueryModel
{
    public string? Cursor { get; set; }
    [Range(1, 200)] public int Count { get; set; } = 50;
    public string? Level { get; set; } = "All";
    public Guid? CorrelationId { get; set; }
    public string? Logger { get; set; }
    public string? EventCode { get; set; }
    public string? Keyword { get; set; }
    public Guid? WorkerNodeId { get; set; }
    public Guid? DeploymentTicketId { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
}
