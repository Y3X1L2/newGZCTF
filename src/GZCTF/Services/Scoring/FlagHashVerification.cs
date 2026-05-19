using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;

namespace GZCTF.Services.Scoring;

public class FlagHashVerification : IVerificationStrategy
{
    public VerificationMode HandledMode => VerificationMode.AutoExact;

    public Task<VerificationResult> VerifyAsync(string answer, ScoringRule rule, AppDbContext context, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(rule.ExpectedAnswerHash))
            return Task.FromResult(new VerificationResult(AnswerResult.WrongAnswer, 0));

        var hash = answer.ToSHA256String();
        return Task.FromResult(
            string.Equals(hash, rule.ExpectedAnswerHash, StringComparison.OrdinalIgnoreCase)
                ? new VerificationResult(AnswerResult.Accepted, 100)
                : new VerificationResult(AnswerResult.WrongAnswer, 0));
    }
}
