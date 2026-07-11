using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Application;

public sealed record IdempotencyBeginResult(ApiOperation Operation, bool Reused);

public sealed class IdempotencyService(
    IApiOperationStore store,
    ExternalApiAuditContext? auditContext = null)
{
    public Task<IdempotencyBeginResult> BeginAsync(
        Guid apiTokenId,
        string routeKey,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken) =>
        BeginAsync(
            apiTokenId,
            null,
            routeKey,
            routeKey,
            idempotencyKey,
            requestHash,
            cancellationToken);

    public async Task<IdempotencyBeginResult> BeginAsync(
        Guid apiTokenId,
        Guid? actorUserId,
        string kind,
        string routeKey,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        Validate(kind, routeKey, idempotencyKey, requestHash);

        var existing = await store.FindIdempotentAsync(
            apiTokenId, routeKey, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var reused = Reuse(existing, requestHash);
            auditContext?.SetOperation(reused.Operation.Id, true);
            return reused;
        }

        var now = DateTimeOffset.UtcNow;
        var operation = new ApiOperation
        {
            Kind = kind.Trim(),
            ActorUserId = actorUserId,
            ApiTokenId = apiTokenId,
            RouteKey = routeKey.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
            RequestHash = requestHash.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await store.AddAsync(operation, cancellationToken);
            var created = new IdempotencyBeginResult(operation, false);
            auditContext?.SetOperation(operation.Id, false);
            return created;
        }
        catch (ApiOperationAlreadyExistsException)
        {
            existing = await store.FindIdempotentAsync(
                apiTokenId, operation.RouteKey, operation.IdempotencyKey, cancellationToken);
            if (existing is null)
                throw;

            var reused = Reuse(existing, operation.RequestHash);
            auditContext?.SetOperation(reused.Operation.Id, true);
            return reused;
        }
    }

    private static IdempotencyBeginResult Reuse(ApiOperation operation, string requestHash)
    {
        if (!string.Equals(operation.RequestHash, requestHash.Trim(), StringComparison.Ordinal))
            throw new IdempotencyConflictException();

        return new IdempotencyBeginResult(operation, true);
    }

    private static void Validate(string kind, string routeKey, string idempotencyKey, string requestHash)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new IdempotencyValidationException(
                "idempotency_key_required", "An Idempotency-Key header is required.");
        if (idempotencyKey.Trim().Length > 128)
            throw new IdempotencyValidationException(
                "idempotency_key_invalid", "Idempotency-Key cannot exceed 128 characters.");
        if (string.IsNullOrWhiteSpace(kind) || kind.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(routeKey) || routeKey.Trim().Length > 256)
            throw new IdempotencyValidationException(
                "operation_route_invalid", "The operation kind or route key is invalid.");
        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Trim().Length > 128)
            throw new IdempotencyValidationException(
                "request_hash_invalid", "The request hash is invalid.");
    }
}
