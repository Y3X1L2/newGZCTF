using System.Threading;
using System.Threading.Tasks;
using GZCTF.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class AwdpRoundServiceLifecycleTests
{
    [Fact]
    public async Task StopAsync_IsSafeAfterServiceWasAlreadyDisposed()
    {
        var service = new AwdpRoundService(
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<ILogger<AwdpRoundService>>());
        service.Dispose();

        await service.StopAsync(CancellationToken.None);
    }
}
