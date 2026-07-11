using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class EfImageImportSubmissionStore(
    AppDbContext context,
    ExternalApiAuditContext auditContext) : IImageImportSubmissionStore
{
    public async Task<IdempotencyBeginResult> SubmitAsync(
        ImageImportSubmission submission,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(submission, cancellationToken);
        if (existing is not null)
        {
            var reused = Reuse(existing, submission.RequestHash);
            auditContext.SetOperation(reused.Operation.Id, true);
            return reused;
        }

        var now = DateTimeOffset.UtcNow;
        var operation = new ApiOperation
        {
            Kind = ImageImportApplicationService.OperationKind,
            ActorUserId = submission.ActorUserId,
            ApiTokenId = submission.ApiTokenId,
            RouteKey = submission.RouteKey,
            IdempotencyKey = submission.IdempotencyKey.Trim(),
            RequestHash = submission.RequestHash,
            CreatedAt = now,
            UpdatedAt = now
        };
        submission.Job.OperationId = operation.Id;
        context.AddRange(operation, submission.Job);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            var created = new IdempotencyBeginResult(operation, false);
            auditContext.SetOperation(operation.Id, false);
            return created;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            existing = await FindExistingAsync(submission, cancellationToken);
            if (existing is null)
                throw;
            var reused = Reuse(existing, submission.RequestHash);
            auditContext.SetOperation(reused.Operation.Id, true);
            return reused;
        }
    }

    private Task<ApiOperation?> FindExistingAsync(
        ImageImportSubmission submission,
        CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(
            operation => operation.ApiTokenId == submission.ApiTokenId &&
                         operation.RouteKey == submission.RouteKey &&
                         operation.IdempotencyKey == submission.IdempotencyKey.Trim(),
            cancellationToken);

    private static IdempotencyBeginResult Reuse(ApiOperation operation, string requestHash)
    {
        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException();
        return new IdempotencyBeginResult(operation, true);
    }
}
