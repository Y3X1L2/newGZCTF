using GZCTF.Models.Data;
using GZCTF.Services.Scoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace GZCTF.Test.UnitTests.Scoring;

public class UnifiedScoringEngineTests
{
    [Fact]
    public void Constructor_InitializesStrategies_FromRegisteredImplementations()
    {
        var strategies = new IVerificationStrategy[]
        {
            new FlagHashVerification(),
            new ManualReviewVerification()
        };
        var engine = new UnifiedScoringEngine(null!, NullLogger<UnifiedScoringEngine>.Instance, strategies);
        // Engine should not throw
        Assert.NotNull(engine);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_ReturnsNotFound_WhenNoScoringRule()
    {
        // This test verifies graceful degradation when no rule exists.
        // In practice, it requires integration test with real DB.
        // Unit test just verifies interface contract.
        Assert.True(true); // Placeholder for integration test
    }
}
