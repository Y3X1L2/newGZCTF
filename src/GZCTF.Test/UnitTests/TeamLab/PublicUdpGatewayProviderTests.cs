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
    public void Config_DefaultProvider_IsNftables()
    {
        Assert.Equal("nftables", new PublicUdpGatewayConfig().Provider);
    }

    [Fact]
    public async Task DisabledProvider_MarksRuleUnsyncedAndRefusesToReportSuccess()
    {
        var provider = new PublicUdpGatewayProvider(
            Options.Create(new PublicUdpGatewayConfig { Enable = false, Provider = "nftables" }),
            NullLogger<PublicUdpGatewayProvider>.Instance);

        var mapping = new TeamLabPublicUdpMapping
        {
            PublicUdpPort = 32001,
            WorkerTunnelIp = "10.250.0.10",
            WorkerWireGuardPort = 42001
        };

        var result = await provider.SyncMappingAsync(mapping, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not enabled", result.Message);
        Assert.Contains("32001", string.Join('\n', result.Commands));
        Assert.Contains("dnat ip to 10.250.0.10:42001", string.Join('\n', result.Commands));
        Assert.False(mapping.IsSynced);
        Assert.NotNull(mapping.LastSyncError);
    }

    [Fact]
    public async Task SyncMapping_BuildsIdempotentReplaceCommands()
    {
        var provider = new PublicUdpGatewayProvider(
            Options.Create(new PublicUdpGatewayConfig { Enable = false, Provider = "iptables" }),
            NullLogger<PublicUdpGatewayProvider>.Instance);

        var mapping = new TeamLabPublicUdpMapping
        {
            PublicUdpPort = 32001,
            WorkerTunnelIp = "10.250.0.10",
            WorkerWireGuardPort = 42001
        };

        var result = await provider.SyncMappingAsync(mapping, CancellationToken.None);
        var commands = string.Join('\n', result.Commands);

        Assert.Contains("-D PREROUTING", commands);
        Assert.Contains("-A PREROUTING", commands);
        Assert.Contains("-D POSTROUTING", commands);
        Assert.Contains("-A POSTROUTING", commands);
        Assert.True(commands.IndexOf("-D PREROUTING", System.StringComparison.Ordinal) <
                    commands.IndexOf("-A PREROUTING", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveMapping_BuildsExecutableIptablesDeleteCommands()
    {
        var provider = new PublicUdpGatewayProvider(
            Options.Create(new PublicUdpGatewayConfig { Enable = false, Provider = "iptables" }),
            NullLogger<PublicUdpGatewayProvider>.Instance);

        var mapping = new TeamLabPublicUdpMapping
        {
            PublicUdpPort = 32001,
            WorkerTunnelIp = "10.250.0.10",
            WorkerWireGuardPort = 42001,
            IsSynced = true
        };

        var result = await provider.RemoveMappingAsync(mapping, CancellationToken.None);
        var commands = string.Join('\n', result.Commands);

        Assert.True(result.Success);
        Assert.Contains("-D PREROUTING", commands);
        Assert.Contains("-D POSTROUTING", commands);
        Assert.DoesNotContain("# remove public UDP mapping", commands);
        Assert.False(mapping.IsSynced);
    }

    [Fact]
    public void ShouldWarnForCommandFailure_SuppressesMissingIptablesDeleteRule()
    {
        const string command = "/usr/sbin/iptables -t nat -D PREROUTING -p udp --dport 32004";
        const string output = "iptables: Bad rule (does a matching rule exist in that chain?).";

        Assert.False(PublicUdpGatewayProvider.ShouldWarnForCommandFailure(command, output));
        Assert.True(PublicUdpGatewayProvider.ShouldWarnForCommandFailure(
            "/usr/sbin/iptables -t nat -A PREROUTING -p udp --dport 32004",
            output));
    }
}
