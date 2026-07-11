using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Application;

public sealed class ApiOperationService(IApiOperationStore store)
{
    public Task<ApiOperation?> GetForTokenAsync(
        Guid id,
        Guid apiTokenId,
        CancellationToken cancellationToken) =>
        store.GetForTokenAsync(id, apiTokenId, cancellationToken);

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
