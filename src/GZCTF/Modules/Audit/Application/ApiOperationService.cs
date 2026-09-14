using GZCTF.Modules.Audit.Domain;
using GZCTF.Infrastructure.Persistence.Queries;

namespace GZCTF.Modules.Audit.Application;

public sealed class ApiOperationService(IApiOperationStore store)
{
    public async Task<ApiOperationPageResult> ListForTokenAsync(
        Guid apiTokenId,
        ApiOperationStatus? status,
        string? kind,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        if (apiTokenId == Guid.Empty || limit is < 1 or > 100 ||
            status.HasValue && !Enum.IsDefined(status.Value))
            throw new ApiOperationQueryException(
                "operation_filter_invalid", "The operation filter is invalid.");
        var normalizedKind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim();
        if (normalizedKind?.Length > 128)
            throw new ApiOperationQueryException(
                "operation_filter_invalid", "The operation kind filter is invalid.");

        var cursor = DecodeCursor(after);
        var rows = await store.ListForTokenAsync(
            apiTokenId,
            status,
            normalizedKind,
            cursor?.Time,
            cursor?.Id,
            limit + 1,
            cancellationToken);
        var items = rows.Take(limit).ToArray();
        var nextCursor = rows.Count > limit
            ? new GuidTimeCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;
        return new ApiOperationPageResult(items, nextCursor);
    }

    public async Task<ApiOperation?> GetAccessibleAsync(
        Guid id,
        Guid apiTokenId,
        Guid actorUserId,
        bool isAdministrator,
        bool hasExplicitGrant,
        CancellationToken cancellationToken)
    {
        var operation = await store.GetAsync(id, cancellationToken);
        if (operation is null)
            return null;
        // Open API operations are scoped to the credential that submitted the command. A shared
        // platform user may own tokens for different TeamLab scopes, so actor identity alone must
        // not make one token's operation history visible to another token.
        return operation.ApiTokenId == apiTokenId || isAdministrator || hasExplicitGrant
            ? operation
            : null;
    }

    public Task<IReadOnlyList<ApiOperation>> ClaimAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int count,
        CancellationToken cancellationToken) =>
        store.ClaimAsync(leaseOwner, leaseDuration, count, cancellationToken);

    public Task<bool> RenewLeaseAsync(
        Guid id,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        store.RenewLeaseAsync(id, leaseOwner, leaseDuration, cancellationToken);

    public Task<bool> CompleteAsync(
        Guid id,
        string leaseOwner,
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken) =>
        store.CompleteAsync(id, leaseOwner, resourceType, resourceId, cancellationToken);

    public Task<bool> UpdateProgressAsync(
        Guid id,
        string leaseOwner,
        string stage,
        long currentProgress,
        long totalProgress,
        string? resourceType,
        string? resourceId,
        Guid? deploymentQueueTicketId,
        CancellationToken cancellationToken) =>
        store.UpdateProgressAsync(
            id,
            leaseOwner,
            stage,
            currentProgress,
            totalProgress,
            resourceType,
            resourceId,
            deploymentQueueTicketId,
            cancellationToken);

    public Task<bool> DeferAsync(
        Guid id,
        string leaseOwner,
        string stage,
        string reasonCode,
        string reasonDetail,
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        store.DeferAsync(id, leaseOwner, stage, reasonCode, reasonDetail, delay, cancellationToken);

    public Task<bool> RetryOrFailAsync(
        Guid id,
        string leaseOwner,
        int maxAttempts,
        string errorCode,
        string errorDetail,
        TimeSpan retryDelay,
        CancellationToken cancellationToken) =>
        store.RetryOrFailAsync(
            id, leaseOwner, maxAttempts, errorCode, errorDetail, retryDelay, cancellationToken);

    private static GuidTimeCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return GuidTimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new ApiOperationQueryException(
                "operation_cursor_invalid", "The operation cursor is invalid.");
        }
    }
}

public sealed record ApiOperationPageResult(
    IReadOnlyList<ApiOperation> Items,
    string? NextCursor);
