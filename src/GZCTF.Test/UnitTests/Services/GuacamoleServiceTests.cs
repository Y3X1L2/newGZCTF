using GZCTF.Services;
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
            password: "qwer1234!");

        Assert.Equal("vm-team-1", data.Name);
        Assert.Equal("rdp", data.Protocol);
        Assert.Equal("10.24.0.30", data.Parameters["hostname"]);
        Assert.Equal("3389", data.Parameters["port"]);
        Assert.Equal("player", data.Parameters["username"]);
        Assert.Equal("qwer1234!", data.Parameters["password"]);
        Assert.Equal("false", data.Parameters["disable-clipboard"]);
        Assert.Equal("true", data.Parameters["enable-clipboard"]);
        Assert.Equal("2", data.Attributes["max-connections-per-user"]);
    }
}
