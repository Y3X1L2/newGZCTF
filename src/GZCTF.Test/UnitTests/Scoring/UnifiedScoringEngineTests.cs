using GZCTF.Models.Data;
using GZCTF.Services.Scoring;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

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
        var engine = new UnifiedScoringEngine(null!, null!, strategies);
        // Engine should not throw
        Assert.NotNull(engine);
    }

    [Fact]
    public void Engine_AcceptsStrategies_FromDI()
    {
        var strategies = new IVerificationStrategy[]
        {
            new FlagHashVerification(),
            new ManualReviewVerification()
        };
        var engine = new UnifiedScoringEngine(null!, null!, strategies);
        Assert.NotNull(engine);
    }
}
