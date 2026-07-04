using System;
using System.Collections.Generic;
using System.IO;
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
    private const string ValidInterfacePrivateKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string ValidPeerPublicKey = "ISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0A=";

    [Fact]
    public async Task CreateBridgeAsync_DryRunReturnsCommandsWithoutExecution()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateBridgeAsync(new TeamLabBridgeRequest(
            RuntimeId: 123,
            BridgeName: "tl123-dmz",
            Cidr: "10.180.1.0/24",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Contains(result.Commands, command => command.Contains("ip link add tl123-dmz type bridge"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("ip addr add"));
    }

    [Fact]
    public async Task CreateBridgeAsync_RejectsUnsafeLinuxResourceName()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateBridgeAsync(new TeamLabBridgeRequest(
            RuntimeId: 123,
            BridgeName: "tl123;rm",
            Cidr: "10.180.1.0/24",
            DryRun: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Message);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task CreateRouterAsync_DryRunConfiguresGatewayAddressesInsideNamespace()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateRouterAsync(new TeamLabRouterRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            Interfaces:
            [
                new TeamLabRouterInterfaceRequest("tl123-entry", "10.180.1.1/24"),
                new TeamLabRouterInterfaceRequest("tl123-lab", "10.180.2.1/24")
            ],
            Routes: [],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands, command => command.Contains("ip netns exec tlr123 ip link set lo up"));
        Assert.Contains(result.Commands, command => command.Contains("ip netns exec tlr123 ip addr add 10.180.1.1/24 dev"));
        Assert.Contains(result.Commands, command => command.Contains("ip netns exec tlr123 ip addr add 10.180.2.1/24 dev"));
        Assert.DoesNotContain(result.Commands, command =>
            command.Contains("ip addr add") && !command.Contains("ip netns exec tlr123"));
    }

    [Fact]
    public async Task CreateRouterAsync_DryRunConfiguresStaticRoutesInsideNamespace()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateRouterAsync(new TeamLabRouterRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            Interfaces:
            [
                new TeamLabRouterInterfaceRequest("yybabc", "10.60.0.14/28")
            ],
            Routes:
            [
                new TeamLabStaticRouteRequest("10.60.0.16/28", "10.60.0.5")
            ],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip route replace 10.60.0.16/28 via 10.60.0.5"));
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_DryRunBuildsPeerCommand()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            InterfaceName: "tlwg123",
            ListenPort: 42001,
            AddressCidr: "10.250.0.10/32",
            InterfacePrivateKey: ValidInterfacePrivateKey,
            PeerPublicKey: ValidPeerPublicKey,
            PeerClientAddress: "10.250.0.2/32",
            PeerAllowedIps: "10.60.0.0/28,10.60.0.16/28",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands, command => command.Contains("printf '<redacted>'"));
        Assert.Contains(result.Commands, command => command.Contains("ip link set tlwg123 netns tlr123"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr add 10.250.0.10/32 dev tlwg123"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 wg set tlwg123 private-key /dev/stdin"));
        Assert.Contains(result.Commands, command => command.Contains("listen-port 42001"));
        Assert.Contains(result.Commands, command => command.Contains("allowed-ips 10.250.0.2/32"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("allowed-ips 10.60.0.0/28"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip route replace 10.250.0.2/32 dev tlwg123"));
        Assert.DoesNotContain(result.Commands,
            command => command.Contains("ip route replace 10.60.0.0/28 dev tlwg123"));
        Assert.DoesNotContain(result.Commands, command => command.StartsWith("wg set ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Commands, command => command.Contains(ValidInterfacePrivateKey));
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_RejectsPlaceholderKeys()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            InterfaceName: "tlwg123",
            ListenPort: 42001,
            AddressCidr: "10.250.0.10/32",
            InterfacePrivateKey: "dry-run-peer-key",
            PeerPublicKey: "dry-run-peer-key",
            PeerClientAddress: "10.180.1.2/32",
            PeerAllowedIps: "10.180.1.2/32",
            DryRun: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("WireGuard", result.Message);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task ProbeAsync_DryRunBuildsNamespacePingProbe()
    {
        var service = CreateService(enable: false);

        var result = await service.ProbeAsync(new TeamLabProbeRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            TargetIp: "10.180.1.2",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ping -c 1 -W 2 10.180.1.2"));
    }

    [Fact]
    public async Task AttachContainerAsync_DryRunBuildsVethAttachmentWithoutFabricNaming()
    {
        var service = CreateService(enable: false);

        var result = await service.AttachContainerAsync(new TeamLabContainerAttachRequest(
            RuntimeId: 123,
            ContainerId: "abcdef123456",
            BridgeName: "tl123-dmz",
            HostInterfaceName: "tl123h0",
            ContainerInterfaceName: "eth0",
            AddressCidr: "10.180.1.10/24",
            MacAddress: "02:42:ac:10:00:02",
            RemoveDefaultRoute: true,
            GatewayIp: "10.180.1.1",
            StaticRoutes: ["10.180.2.0/24", "10.250.0.2/32"],
            DnsServers: ["10.180.1.1"],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Contains(result.Commands, command => command.Contains("docker inspect -f '{{.State.Pid}}'"));
        Assert.Contains(result.Commands, command => command.Contains("ip link add 'tl123h0' type veth peer name"));
        Assert.Contains(result.Commands, command => command.Contains("ip link set 'tl123h0' master 'tl123-dmz'"));
        Assert.Contains(result.Commands, command => command.Contains("ip addr add '10.180.1.10/24' dev 'eth0'"));
        Assert.Contains(result.Commands, command => command.Contains("ip link set dev 'eth0' address '02:42:ac:10:00:02'"));
        Assert.Contains(result.Commands, command => command.Contains("ip route del default"));
        Assert.Contains(result.Commands, command => command.Contains("ip route replace '10.180.2.0/24' via '10.180.1.1' dev 'eth0'"));
        Assert.Contains(result.Commands, command => command.Contains("ip route replace '10.250.0.2/32' via '10.180.1.1' dev 'eth0'"));
        Assert.Contains(result.Commands, command =>
            command.Contains("nameserver 10.180.1.1") && command.Contains("/etc/resolv.conf"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("/fabric/"));
    }

    [Fact]
    public async Task ConfigureDhcpDnsAsync_DryRunBuildsDnsmasqStaticLeaseCommands()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureDhcpDnsAsync(new TeamLabDhcpDnsRequest(
            RuntimeId: 123,
            ServiceName: "tldns123",
            NamespaceName: "tlr123",
            BridgeName: "tl123-data",
            InterfaceName: "tlr123n0",
            GatewayIp: "10.180.1.1",
            Cidr: "10.180.1.0/24",
            Domain: "team123.lab",
            Leases:
            [
                new TeamLabDhcpLeaseRequest("02:42:ac:10:00:02", "10.180.1.10", "portal"),
                new TeamLabDhcpLeaseRequest("02:42:ac:10:00:03", "10.180.1.20", "win-ad")
            ],
            DnsRecords:
            [
                new TeamLabDnsRecordRequest("portal", "10.180.1.10"),
                new TeamLabDnsRecordRequest("win-ad", "10.180.1.20")
            ],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Contains(result.Commands, command => command.Contains("dnsmasq"));
        Assert.Contains(result.Commands, command => command.Contains("ip netns exec tlr123 dnsmasq"));
        Assert.Contains(result.Commands, command => command.Contains("--interface=tlr123n0"));
        Assert.Contains(result.Commands, command => command.Contains("--dhcp-range=10.180.1.1,static,255.255.255.0"));
        Assert.Contains(result.Commands, command => command.Contains("02:42:ac:10:00:02,10.180.1.10,portal"));
        Assert.Contains(result.Commands, command => command.Contains("address=/portal.team123.lab/10.180.1.10"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("virbr0"));
    }

    [Fact]
    public async Task ProbeDhcpDnsAsync_DryRunBuildsNamespaceDnsProbe()
    {
        var service = CreateService(enable: false);

        var result = await service.ProbeDhcpDnsAsync(new TeamLabDhcpDnsProbeRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            GatewayIp: "10.180.1.1",
            Hostname: "portal.team123.lab",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 nslookup portal.team123.lab 10.180.1.1"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 nc -uz -w 2 10.180.1.1 53"));
    }

    [Fact]
    public async Task CreateBridgeAsync_DisabledExecutionReportsCapabilityDisabled()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateBridgeAsync(new TeamLabBridgeRequest(
            RuntimeId: 123,
            BridgeName: "tl123-dmz",
            Cidr: "10.180.1.0/24",
            DryRun: false), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Contains("disabled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_RealExecutionStreamsPrivateKeyToWireGuard()
    {
        var runner = new PrivateKeyAwareTeamLabCommandRunner(ValidInterfacePrivateKey);
        var service = new TeamLabNetworkService(
            Options.Create(new AgentTeamLabConfig { Enable = true, DryRun = false }),
            runner,
            NullLogger<TeamLabNetworkService>.Instance);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            InterfaceName: "tlwg123",
            ListenPort: 42001,
            AddressCidr: "10.180.1.254/32",
            InterfacePrivateKey: ValidInterfacePrivateKey,
            PeerPublicKey: ValidPeerPublicKey,
            PeerClientAddress: "10.180.1.2/32",
            PeerAllowedIps: "10.180.1.2/32",
            DryRun: false), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.DryRun);
        Assert.DoesNotContain(runner.Commands, command => command.Contains("printf '<redacted>'"));
        Assert.Contains(runner.Commands, command => command.Contains("wg set tlwg123 private-key /dev/stdin"));
        Assert.Contains(runner.StandardInputs, input => input == ValidInterfacePrivateKey);
        Assert.DoesNotContain(runner.Commands, command => command.Contains("/run/gzctf-teamlab"));
    }

    [Fact]
    public void TeamLabCommandRunner_CreatesProcessWithStableRootWorkingDirectory()
    {
        var startInfo = TeamLabCommandRunner.CreateStartInfo("pwd", redirectStandardInput: true);

        Assert.Equal("/bin/sh", startInfo.FileName);
        Assert.Equal("/", startInfo.WorkingDirectory);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["-c", "pwd"], startInfo.ArgumentList);
    }

    private static TeamLabNetworkService CreateService(bool enable) => new(
        Options.Create(new AgentTeamLabConfig { Enable = enable, DryRun = true }),
        new TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance),
        NullLogger<TeamLabNetworkService>.Instance);

    private sealed class PrivateKeyAwareTeamLabCommandRunner(string privateKey) : TeamLabCommandRunner(
        NullLogger<TeamLabCommandRunner>.Instance)
    {
        public List<string> Commands { get; } = [];
        public List<string?> StandardInputs { get; } = [];

        public override Task<(bool Success, string Output)> RunAsync(string command, CancellationToken token)
        {
            return RunAsync(command, null, token);
        }

        public override Task<(bool Success, string Output)> RunAsync(string command, string? standardInput,
            CancellationToken token)
        {
            Commands.Add(command);
            StandardInputs.Add(standardInput);

            if (command.Contains("wg set tlwg123 private-key", StringComparison.Ordinal) &&
                standardInput != privateKey)
            {
                return Task.FromResult((false, "private key was not streamed when WireGuard was configured"));
            }

            return Task.FromResult((true, string.Empty));
        }
    }
}
