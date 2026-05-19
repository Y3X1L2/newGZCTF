using GZCTF.Models.Data;
using GZCTF.Services.Scoring;
using GZCTF.Utils;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.Scoring;

public class FlagHashVerificationTests
{
    [Fact]
    public async Task Verify_ReturnsAccepted_WhenHashMatches()
    {
        var strategy = new FlagHashVerification();
        var flag = "flag{test123}";
        var rule = new ScoringRule
        {
            ExpectedAnswerHash = flag.ToSHA256String(),
            SubmissionType = ScoringSubmissionType.Flag
        };
        var result = await strategy.VerifyAsync(flag, rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.Accepted, result.Status);
    }

    [Fact]
    public async Task Verify_ReturnsWrongAnswer_WhenNoHashConfigured()
    {
        var strategy = new FlagHashVerification();
        var rule = new ScoringRule { ExpectedAnswerHash = null };
        var result = await strategy.VerifyAsync("anything", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);
    }
}

public class RegexVerificationTests
{
    [Fact]
    public async Task Verify_ReturnsAccepted_WhenPatternMatches()
    {
        var strategy = new RegexVerification();
        var rule = new ScoringRule
        {
            VerificationConfig = """{"Pattern":"^CTF\\{[A-F0-9]{8}\\}$"}"""
        };
        var result = await strategy.VerifyAsync("CTF{DEADBEEF}", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.Accepted, result.Status);
    }

    [Fact]
    public async Task Verify_ReturnsWrongAnswer_WhenConfigIsNotJson()
    {
        var strategy = new RegexVerification();
        var rule = new ScoringRule { VerificationConfig = "not-json" };
        var result = await strategy.VerifyAsync("anything", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);
    }
}

public class ManualReviewVerificationTests
{
    [Fact]
    public async Task Verify_ReturnsFlagSubmitted_ForAnyInput()
    {
        var strategy = new ManualReviewVerification();
        var result = await strategy.VerifyAsync("anything", new ScoringRule(), null!, CancellationToken.None);
        Assert.Equal(AnswerResult.FlagSubmitted, result.Status);
        Assert.Equal(0, result.Score);
    }
}

public class ScriptVerificationTests
{
    [Fact]
    public async Task Verify_ReturnsAccepted_WhenScriptExitsZero()
    {
        var strategy = new ScriptVerification(null!);
        var rule = new ScoringRule
        {
            VerificationConfig = """{"ScriptPath":"echo","ScriptArgs":"success"}"""
        };
        var result = await strategy.VerifyAsync("any", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.Accepted, result.Status);
    }
}
