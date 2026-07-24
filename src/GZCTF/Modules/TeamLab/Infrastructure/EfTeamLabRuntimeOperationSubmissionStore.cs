using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class EfTeamLabRuntimeOperationSubmissionStore(
    AppDbContext context,
    ExternalApiAuditContext auditContext) : ITeamLabRuntimeOperationSubmissionStore
{
    public async Task<IdempotencyBeginResult> SubmitAsync(
        TeamLabRuntimeOperationSubmission submission,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(submission, cancellationToken);
        if (existing is not null) return Reuse(existing, submission.RequestHash);
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (transaction is not null && context.Database.IsNpgsql() && submission is
            { ResourceType: "teamlab-runtime", ResourceId: not null })
        {
            var resourceLock = $"{submission.ResourceType}:{submission.ResourceId}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({resourceLock}, 0))", cancellationToken);
        }
        existing = await FindExistingAsync(submission, cancellationToken);
        if (existing is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Reuse(existing, submission.RequestHash);
        }
        if (submission is { ResourceType: "teamlab-runtime", ResourceId: not null })
        {
            var active = await context.ApiOperations.AsNoTracking().AnyAsync(operation =>
                operation.Kind == TeamLabRuntimeOperationApplicationService.OperationKind &&
                operation.ResourceType == submission.ResourceType && operation.ResourceId == submission.ResourceId &&
                (operation.Status == ApiOperationStatus.Pending || operation.Status == ApiOperationStatus.Running),
                cancellationToken);
            if (active)
                throw new TeamLabApiContractException(
                    "runtime_operation_in_progress",
                    "Another lifecycle operation is already running for this TeamLab runtime.",
                    409);
        }
        var now = DateTimeOffset.UtcNow;
        var operation = new ApiOperation
        {
            Kind = TeamLabRuntimeOperationApplicationService.OperationKind,
            ActorUserId = submission.ActorUserId,
            ApiTokenId = submission.ApiTokenId,
            RouteKey = submission.RouteKey,
            IdempotencyKey = submission.IdempotencyKey,
            RequestHash = submission.RequestHash,
            ResourceType = submission.ResourceType,
            ResourceId = submission.ResourceId,
            CreatedAt = now,
            UpdatedAt = now
        };
        submission.Job.OperationId = operation.Id;
        context.AddRange(operation, submission.Job);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            auditContext.SetOperation(operation.Id, false);
            return new IdempotencyBeginResult(operation, false);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            existing = await FindExistingAsync(submission, cancellationToken);
            if (existing is null) throw;
            return Reuse(existing, submission.RequestHash);
        }
    }

    private Task<ApiOperation?> FindExistingAsync(
        TeamLabRuntimeOperationSubmission submission,
        CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(operation =>
            operation.ApiTokenId == submission.ApiTokenId && operation.RouteKey == submission.RouteKey &&
            operation.IdempotencyKey == submission.IdempotencyKey, cancellationToken);

    private IdempotencyBeginResult Reuse(ApiOperation operation, string requestHash)
    {
        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException();
        auditContext.SetOperation(operation.Id, true);
        return new IdempotencyBeginResult(operation, true);
    }
}
