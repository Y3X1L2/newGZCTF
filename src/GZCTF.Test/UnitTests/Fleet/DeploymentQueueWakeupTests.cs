using System;
using System.Diagnostics;
using System.Threading.Tasks;
using GZCTF.Modules.Runtime.Infrastructure;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public sealed class DeploymentQueueWakeupTests
{
    [Fact]
    public async Task PollingFallback_WaitsForBoundedIntervalWithoutOwningTicketState()
    {
        var wakeup = new PollingDeploymentQueueWakeup();
        var stopwatch = Stopwatch.StartNew();

        await wakeup.NotifyAsync(Guid.NewGuid());
        await wakeup.WaitAsync(TimeSpan.FromMilliseconds(20));

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(15));
    }
}
