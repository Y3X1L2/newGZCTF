using GZCTF.Modules.Audit.Domain;
using System.Text.Json;

namespace GZCTF.Modules.Audit.Contracts;

public sealed record ApiOperationModel(
    Guid Id,
    string Kind,
    ApiOperationStatus Status,
    string Stage,
    string? ResourceType,
    string? ResourceId,
    Guid? DeploymentQueueTicketId,
    long CurrentProgress,
    long TotalProgress,
    int AttemptCount,
    string? ErrorCode,
    string? ErrorDetail,
    JsonElement? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt)
{
    public static ApiOperationModel FromEntity(ApiOperation operation, JsonElement? result = null) => new(
        operation.Id,
        operation.Kind,
        operation.Status,
        operation.Stage,
        operation.ResourceType,
        operation.ResourceId,
        operation.DeploymentQueueTicketId,
        operation.CurrentProgress,
        operation.TotalProgress,
        operation.AttemptCount,
        operation.ErrorCode,
        operation.ErrorDetail,
        result,
        operation.CreatedAt,
        operation.StartedAt,
        operation.UpdatedAt,
        operation.CompletedAt);
}
