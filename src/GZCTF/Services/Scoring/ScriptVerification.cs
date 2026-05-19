using System.Diagnostics;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.Extensions.Logging;

namespace GZCTF.Services.Scoring;

public class ScriptVerification : IVerificationStrategy
{
    private readonly ILogger _logger;
    public VerificationMode HandledMode => VerificationMode.AutoScript;

    public ScriptVerification(ILogger<ScriptVerification> logger) => _logger = logger;

    public async Task<VerificationResult> VerifyAsync(string answer, ScoringRule rule, AppDbContext context, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(rule.VerificationConfig))
            return new VerificationResult(AnswerResult.WrongAnswer, 0);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rule.VerificationConfig);
            var scriptPath = doc.RootElement.TryGetProperty("ScriptPath", out var sp) ? sp.GetString() : null;
            if (string.IsNullOrWhiteSpace(scriptPath))
                return new VerificationResult(AnswerResult.WrongAnswer, 0);

            var scriptArgs = doc.RootElement.TryGetProperty("ScriptArgs", out var sa) ? sa.GetString() : "";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = scriptPath,
                    Arguments = scriptArgs ?? "",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            process.Start();
            await process.WaitForExitAsync(cts.Token);

            return process.ExitCode == 0
                ? new VerificationResult(AnswerResult.Accepted, 100)
                : new VerificationResult(AnswerResult.WrongAnswer, 0);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Script verification timed out for rule {RuleId}", rule.Id);
            return new VerificationResult(AnswerResult.WrongAnswer, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Script verification failed for rule {RuleId}", rule.Id);
            return new VerificationResult(AnswerResult.WrongAnswer, 0);
        }
    }
}
