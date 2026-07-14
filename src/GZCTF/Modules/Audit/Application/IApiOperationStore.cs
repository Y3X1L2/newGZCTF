using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Application;

public interface IApiOperationStore
{
    Task<ApiOperation?> FindIdempotentAsync(
        Guid apiTokenId,
        string routeKey,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task AddAsync(ApiOperation operation, CancellationToken cancellationToken);

    Task<ApiOperation?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiOperation>> ClaimAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int count,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid id,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid id,
        string leaseOwner,
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken);

    Task<bool> UpdateProgressAsync(
        Guid id,
        string leaseOwner,
        string stage,
        long currentProgress,
        long totalProgress,
        string? resourceType,
        string? resourceId,
        Guid? deploymentQueueTicketId,
        CancellationToken cancellationToken);

    Task<bool> RetryOrFailAsync(
        Guid id,
        string leaseOwner,
        int maxAttempts,
        string errorCode,
        string errorDetail,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);
}
