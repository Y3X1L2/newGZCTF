using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Application;

public sealed class ApiOperationService(IApiOperationStore store)
{
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
}
