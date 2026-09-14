using GZCTF.Extensions.Startup;
using GZCTF.Infrastructure.Telemetry;
using Xunit;

namespace GZCTF.Test.UnitTests.Observability;

public sealed class TelemetryRegistrationTests
{
    [Fact]
    public void ConfiguredMeterNames_IncludeTeamLabMeter()
    {
        Assert.Contains(PlatformTelemetry.TeamLabMeterName, TelemetryExtension.ConfiguredMeterNames);
    }
}
