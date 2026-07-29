using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Exercise.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.Exercise.Infrastructure;

public sealed class EfExerciseMutationSubmissionStore(AppDbContext context)
    : IExerciseMutationSubmissionStore
{
    public async Task<IdempotencyBeginResult> SubmitAsync(
        ExerciseMutationSubmission submission,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(submission, cancellationToken);
        if (existing is not null)
            return Reuse(existing, submission.RequestHash);

        var now = DateTimeOffset.UtcNow;
        var operation = new ApiOperation
        {
            Kind = ExerciseExternalApplicationService.OperationKind,
            ActorUserId = submission.ActorUserId,
            ApiTokenId = submission.ApiTokenId,
            RouteKey = submission.RouteKey,
            IdempotencyKey = submission.IdempotencyKey,
            RequestHash = submission.RequestHash,
            ResourceType = "exercise",
            ResourceId = submission.Job.ExerciseId?.ToString() ?? "*",
            CreatedAt = now,
            UpdatedAt = now
        };
        submission.Job.OperationId = operation.Id;
        context.AddRange(operation, submission.Job);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new IdempotencyBeginResult(operation, false);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            existing = await FindExistingAsync(submission, cancellationToken);
            if (existing is null)
                throw;
            return Reuse(existing, submission.RequestHash);
        }
    }

    Task<ApiOperation?> FindExistingAsync(
        ExerciseMutationSubmission submission,
        CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(operation =>
            operation.ApiTokenId == submission.ApiTokenId &&
            operation.RouteKey == submission.RouteKey &&
            operation.IdempotencyKey == submission.IdempotencyKey,
            cancellationToken);

    static IdempotencyBeginResult Reuse(ApiOperation operation, string requestHash)
    {
        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException();
        return new IdempotencyBeginResult(operation, true);
    }
}
