using GZCTF.Models;
using GZCTF.Models.Data;

namespace GZCTF.Services.Scoring;

/// <summary>
/// Strategy pattern for answer verification.
/// Dispatch key: ScoringRule.VerificationMode (not SubmissionType -- those are orthogonal axes).
/// </summary>
public interface IVerificationStrategy
{
    Task<VerificationResult> VerifyAsync(string answer, ScoringRule rule, AppDbContext context, CancellationToken token);
    VerificationMode HandledMode { get; }
}
