using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

public sealed class RdpPortReadinessTests
{
    [Fact]
    public async Task TcpProbe_OnlyReportsListeningPortAsReady()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.True(await KvmService.IsTcpPortReadyAsync(
            IPAddress.Loopback.ToString(), port, CancellationToken.None));

        listener.Stop();
        Assert.False(await KvmService.IsTcpPortReadyAsync(
            IPAddress.Loopback.ToString(), port, CancellationToken.None));
    }
}
