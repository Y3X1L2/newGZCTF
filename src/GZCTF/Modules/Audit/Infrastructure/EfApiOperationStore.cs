using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class EfApiOperationStore(AppDbContext context) : IApiOperationStore
{
    public Task<ApiOperation?> FindIdempotentAsync(
        Guid apiTokenId,
        string routeKey,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(
            operation => operation.ApiTokenId == apiTokenId &&
                         operation.RouteKey == routeKey &&
                         operation.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task AddAsync(ApiOperation operation, CancellationToken cancellationToken)
    {
        context.ApiOperations.Add(operation);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.Entry(operation).State = EntityState.Detached;
            throw new ApiOperationAlreadyExistsException();
        }
    }

    public Task<ApiOperation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(
            operation => operation.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApiOperation>> ClaimAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int count,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var pending = ApiOperationStatus.Pending;
        var running = ApiOperationStatus.Running;
        var operations = await context.ApiOperations
            .FromSqlInterpolated($"""
                SELECT * FROM "ApiOperations"
                WHERE
                    ("Status" = {pending} AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= CURRENT_TIMESTAMP))
                    OR
                    ("Status" = {running} AND "LeaseExpiresAt" IS NOT NULL AND "LeaseExpiresAt" <= CURRENT_TIMESTAMP)
                ORDER BY "CreatedAt"
                LIMIT {Math.Clamp(count, 1, 64)}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (operations.Count > 0)
        {
            var operationIds = operations.Select(operation => operation.Id).ToArray();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ApiOperations"
                SET
                    "Status" = {running},
                    "Stage" = CASE WHEN "AttemptCount" = 0 THEN 'running' ELSE 'recovering' END,
                    "AttemptCount" = "AttemptCount" + 1,
                    "LeaseOwner" = {leaseOwner},
                    "LeaseExpiresAt" = CURRENT_TIMESTAMP + {leaseDuration},
                    "NextAttemptAt" = NULL,
                    "StartedAt" = COALESCE("StartedAt", CURRENT_TIMESTAMP),
                    "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE "Id" = ANY ({operationIds})
                """, cancellationToken);

            context.ChangeTracker.Clear();
            operations = await context.ApiOperations.AsNoTracking()
                .Where(operation => operationIds.Contains(operation.Id))
                .OrderBy(operation => operation.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return operations;
    }

    public async Task<bool> RenewLeaseAsync(
        Guid id,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
            return false;

        var running = ApiOperationStatus.Running;
        var affectedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ApiOperations"
            SET
                "LeaseExpiresAt" = CURRENT_TIMESTAMP + {leaseDuration},
                "UpdatedAt" = CURRENT_TIMESTAMP
            WHERE
                "Id" = {id}
                AND "Status" = {running}
                AND "LeaseOwner" = {leaseOwner}
                AND "LeaseExpiresAt" > CURRENT_TIMESTAMP
            """, cancellationToken);
        return affectedRows > 0;
    }

    public async Task<bool> CompleteAsync(
        Guid id,
        string leaseOwner,
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        var running = ApiOperationStatus.Running;
        var succeeded = ApiOperationStatus.Succeeded;
        var affectedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ApiOperations"
            SET
                "Status" = {succeeded},
                "Stage" = 'completed',
                "ResourceType" = COALESCE({resourceType}::text, "ResourceType"),
                "ResourceId" = COALESCE({resourceId}::text, "ResourceId"),
                "LeaseOwner" = NULL,
                "LeaseExpiresAt" = NULL,
                "ErrorCode" = NULL,
                "ErrorDetail" = NULL,
                "UpdatedAt" = CURRENT_TIMESTAMP,
                "CompletedAt" = CURRENT_TIMESTAMP
            WHERE
                "Id" = {id}
                AND "Status" = {running}
                AND "LeaseOwner" = {leaseOwner}
                AND "LeaseExpiresAt" > CURRENT_TIMESTAMP
            """, cancellationToken);
        return affectedRows > 0;
    }

    public async Task<bool> UpdateProgressAsync(
        Guid id,
        string leaseOwner,
        string stage,
        long currentProgress,
        long totalProgress,
        string? resourceType,
        string? resourceId,
        Guid? deploymentQueueTicketId,
        CancellationToken cancellationToken)
    {
        var normalizedStage = stage.Trim();
        if (normalizedStage.Length is 0 or > 128)
            throw new ArgumentOutOfRangeException(nameof(stage));

        var running = ApiOperationStatus.Running;
        var affectedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ApiOperations"
            SET
                "Stage" = {normalizedStage},
                "CurrentProgress" = {Math.Max(0, currentProgress)},
                "TotalProgress" = {Math.Max(0, totalProgress)},
                "ResourceType" = COALESCE({resourceType}::text, "ResourceType"),
                "ResourceId" = COALESCE({resourceId}::text, "ResourceId"),
                "DeploymentQueueTicketId" = COALESCE({deploymentQueueTicketId}::uuid, "DeploymentQueueTicketId"),
                "ErrorCode" = NULL,
                "ErrorDetail" = NULL,
                "UpdatedAt" = CURRENT_TIMESTAMP
            WHERE
                "Id" = {id}
                AND "Status" = {running}
                AND "LeaseOwner" = {leaseOwner}
                AND "LeaseExpiresAt" > CURRENT_TIMESTAMP
            """, cancellationToken);
        return affectedRows > 0;
    }

    public async Task<bool> DeferAsync(
        Guid id,
        string leaseOwner,
        string stage,
        string reasonCode,
        string reasonDetail,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        var normalizedStage = stage.Trim();
        if (normalizedStage.Length is 0 or > 128)
            throw new ArgumentOutOfRangeException(nameof(stage));
        var code = reasonCode.Length <= 128 ? reasonCode : reasonCode[..128];
        var detail = reasonDetail.Length <= 2048 ? reasonDetail : reasonDetail[..2048];
        var running = ApiOperationStatus.Running;
        var pending = ApiOperationStatus.Pending;
        return await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ApiOperations"
            SET
                "Status" = {pending},
                "Stage" = {normalizedStage},
                "ErrorCode" = {code},
                "ErrorDetail" = {detail},
                "LeaseOwner" = NULL,
                "LeaseExpiresAt" = NULL,
                "NextAttemptAt" = CURRENT_TIMESTAMP + {delay},
                "UpdatedAt" = CURRENT_TIMESTAMP
            WHERE
                "Id" = {id}
                AND "Status" = {running}
                AND "LeaseOwner" = {leaseOwner}
                AND "LeaseExpiresAt" > CURRENT_TIMESTAMP
            """, cancellationToken) > 0;
    }

    public async Task<bool> RetryOrFailAsync(
        Guid id,
        string leaseOwner,
        int maxAttempts,
        string errorCode,
        string errorDetail,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        if (retryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));

        var running = ApiOperationStatus.Running;
        var pending = ApiOperationStatus.Pending;
        var failed = ApiOperationStatus.Failed;
        var normalizedCode = errorCode.Length <= 128 ? errorCode : errorCode[..128];
        var normalizedDetail = errorDetail.Length <= 2048 ? errorDetail : errorDetail[..2048];
        var affectedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ApiOperations"
            SET
                "Status" = CASE WHEN "AttemptCount" >= {maxAttempts} THEN {failed} ELSE {pending} END,
                "Stage" = CASE WHEN "AttemptCount" >= {maxAttempts} THEN 'failed' ELSE 'retrying' END,
                "ErrorCode" = {normalizedCode},
                "ErrorDetail" = {normalizedDetail},
                "LeaseOwner" = NULL,
                "LeaseExpiresAt" = NULL,
                "NextAttemptAt" = CASE
                    WHEN "AttemptCount" >= {maxAttempts} THEN NULL
                    ELSE CURRENT_TIMESTAMP + {retryDelay}
                END,
                "CompletedAt" = CASE
                    WHEN "AttemptCount" >= {maxAttempts} THEN CURRENT_TIMESTAMP
                    ELSE NULL
                END,
                "UpdatedAt" = CURRENT_TIMESTAMP
            WHERE
                "Id" = {id}
                AND "Status" = {running}
                AND "LeaseOwner" = {leaseOwner}
                AND "LeaseExpiresAt" > CURRENT_TIMESTAMP
            """, cancellationToken);
        return affectedRows > 0;
    }
}
