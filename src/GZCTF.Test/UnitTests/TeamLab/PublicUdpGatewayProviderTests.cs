using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.TeamLab;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class PublicUdpGatewayProviderTests
{
    [Fact]
    public async Task DryRunProvider_MarksRuleUnsyncedButReturnsCommands()
    {
        var provider = new PublicUdpGatewayProvider(
            Options.Create(new PublicUdpGatewayConfig { Enable = false, Provider = "dry-run" }),
            NullLogger<PublicUdpGatewayProvider>.Instance);

        var mapping = new TeamLabPublicUdpMapping
        {
            PublicUdpPort = 32001,
            WorkerTunnelIp = "10.250.0.10",
            WorkerWireGuardPort = 42001
        };

        var result = await provider.SyncMappingAsync(mapping, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("32001", string.Join('\n', result.Commands));
        Assert.False(mapping.IsSynced);
    }
}
