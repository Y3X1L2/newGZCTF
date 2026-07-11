using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Ctf.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.Ctf.Infrastructure;

public sealed class EfChallengeMutationSubmissionStore(
    AppDbContext context,
    ExternalApiAuditContext auditContext) : IChallengeMutationSubmissionStore
{
    public async Task<IdempotencyBeginResult> SubmitAsync(
        ChallengeMutationSubmission submission,
        CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(submission, cancellationToken);
        if (existing is not null)
            return Reuse(existing, submission.RequestHash);

        var now = DateTimeOffset.UtcNow;
        var operation = new ApiOperation
        {
            Kind = ChallengeExternalApplicationService.OperationKind,
            ActorUserId = submission.ActorUserId,
            ApiTokenId = submission.ApiTokenId,
            RouteKey = submission.RouteKey,
            IdempotencyKey = submission.IdempotencyKey,
            RequestHash = submission.RequestHash,
            ResourceType = "game",
            ResourceId = submission.Job.GameId.ToString(),
            CreatedAt = now,
            UpdatedAt = now
        };
        submission.Job.OperationId = operation.Id;
        context.AddRange(operation, submission.Job);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            auditContext.SetOperation(operation.Id, false);
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

    private Task<ApiOperation?> FindExistingAsync(
        ChallengeMutationSubmission submission,
        CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(operation =>
            operation.ApiTokenId == submission.ApiTokenId &&
            operation.RouteKey == submission.RouteKey &&
            operation.IdempotencyKey == submission.IdempotencyKey,
            cancellationToken);

    private IdempotencyBeginResult Reuse(ApiOperation operation, string requestHash)
    {
        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException();
        auditContext.SetOperation(operation.Id, true);
        return new IdempotencyBeginResult(operation, true);
    }
}
