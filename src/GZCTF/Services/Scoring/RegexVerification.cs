using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;

namespace GZCTF.Services.Scoring;

public class RegexVerification : IVerificationStrategy
{
    public VerificationMode HandledMode => VerificationMode.AutoRegex;

    public Task<VerificationResult> VerifyAsync(string answer, ScoringRule rule, AppDbContext context, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(rule.VerificationConfig))
            return Task.FromResult(new VerificationResult(AnswerResult.WrongAnswer, 0));

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rule.VerificationConfig);
            if (doc.RootElement.TryGetProperty("Pattern", out var patternProp))
            {
                var pattern = patternProp.GetString();
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    var regex = new System.Text.RegularExpressions.Regex(pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                    return Task.FromResult(
                        regex.IsMatch(answer)
                            ? new VerificationResult(AnswerResult.Accepted, 100)
                            : new VerificationResult(AnswerResult.WrongAnswer, 0));
                }
            }
        }
        catch { /* invalid JSON -- treated as wrong answer */ }
        return Task.FromResult(new VerificationResult(AnswerResult.WrongAnswer, 0));
    }
}
