using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabCommandBuilderTests
{
    [Fact]
    public async Task CreateBridgeAsync_DryRunReturnsCommandsWithoutExecution()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateBridgeAsync(new TeamLabBridgeRequest(
            RuntimeId: 123,
            BridgeName: "tl123-dmz",
            Cidr: "10.180.1.0/24",
            GatewayIp: "10.180.1.1",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Contains(result.Commands, command => command.Contains("ip link add tl123-dmz type bridge"));
        Assert.Contains(result.Commands, command => command.Contains("10.180.1.1/24"));
    }

    [Fact]
    public async Task CreateBridgeAsync_RejectsUnsafeLinuxResourceName()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateBridgeAsync(new TeamLabBridgeRequest(
            RuntimeId: 123,
            BridgeName: "tl123;rm",
            Cidr: "10.180.1.0/24",
            GatewayIp: "10.180.1.1",
            DryRun: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Message);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_DryRunBuildsPeerCommand()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            InterfaceName: "tlwg123",
            ListenPort: 42001,
            AddressCidr: "10.250.0.10/32",
            PeerPublicKey: "peer-public-key",
            PeerAllowedIps: "10.180.1.2/32",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands, command => command.Contains("wg set tlwg123 listen-port 42001"));
        Assert.Contains(result.Commands, command => command.Contains("allowed-ips 10.180.1.2/32"));
    }

    private static TeamLabNetworkService CreateService(bool enable) => new(
        Options.Create(new AgentTeamLabConfig { Enable = enable, DryRun = true }),
        new TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance),
        NullLogger<TeamLabNetworkService>.Instance);
}
