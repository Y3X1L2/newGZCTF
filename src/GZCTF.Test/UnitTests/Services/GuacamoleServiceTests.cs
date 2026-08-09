using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Internal;
using GZCTF.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class GuacamoleServiceTests
{
    [Fact]
    public void BuildRdpConnectionData_EnablesClipboardAndKeepsSessionIsolated()
    {
        var data = GuacamoleService.BuildRdpConnectionData(
            connectionName: "vm-team-1",
            vmIp: "10.24.0.30",
            rdpPort: 3389,
            username: "player",
            password: "Vm-Credential-Example!");

        Assert.Equal("vm-team-1", data.Name);
        Assert.Equal("rdp", data.Protocol);
        Assert.Equal("10.24.0.30", data.Parameters["hostname"]);
        Assert.Equal("3389", data.Parameters["port"]);
        Assert.Equal("player", data.Parameters["username"]);
        Assert.Equal("Vm-Credential-Example!", data.Parameters["password"]);
        Assert.Equal("false", data.Parameters["disable-copy"]);
        Assert.Equal("false", data.Parameters["disable-paste"]);
        Assert.DoesNotContain("disable-clipboard", data.Parameters.Keys);
        Assert.DoesNotContain("enable-clipboard", data.Parameters.Keys);
        Assert.Equal("2", data.Attributes["max-connections-per-user"]);
    }

    [Fact]
    public async Task GetAuthTokenAsync_WithoutManagedCredentials_FailsClosedWithoutRequest()
    {
        var handler = new RecordingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient("GuacamoleClient"))
            .Returns(new HttpClient(handler));
        var service = new GuacamoleService(
            factory.Object,
            Options.Create(new GuacamoleSettings()),
            NullLogger<GuacamoleService>.Instance);

        var token = await service.GetAuthTokenAsync();

        Assert.Null(token);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
