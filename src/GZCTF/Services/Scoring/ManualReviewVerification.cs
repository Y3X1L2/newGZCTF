using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;

namespace GZCTF.Services.Scoring;

public class ManualReviewVerification : IVerificationStrategy
{
    public VerificationMode HandledMode => VerificationMode.ManualReview;

    public Task<VerificationResult> VerifyAsync(string answer, ScoringRule rule, AppDbContext context, CancellationToken token)
        => Task.FromResult(new VerificationResult(AnswerResult.FlagSubmitted, 0));
}
