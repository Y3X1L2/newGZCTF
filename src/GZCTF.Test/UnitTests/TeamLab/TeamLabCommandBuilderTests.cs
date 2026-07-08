using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public void ResolveTemplatePath_UsesStandardImportedLocalPath()
    {
        using var tempRoot = new TempDirectory();
        var templatePath = Path.Combine(tempRoot.Path, "imported-template.qcow2");
        File.WriteAllText(templatePath, "qcow2");
        var service = CreateKvmService(tempRoot.Path);

        var resolved = service.ResolveTemplatePath(new CreateVmRequest
        {
            TemplateId = 115,
            TemplatePath = templatePath,
            VmName = "tl-test-vm"
        });

        Assert.Equal(Path.GetFullPath(templatePath), resolved);
    }

    [Fact]
    public void ResolveTemplatePath_RejectsTemplateOutsideImageStorage()
    {
        using var tempRoot = new TempDirectory();
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.qcow2");
        File.WriteAllText(outsidePath, "qcow2");
        var service = CreateKvmService(tempRoot.Path);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => service.ResolveTemplatePath(new CreateVmRequest
            {
                TemplatePath = outsidePath,
                VmName = "tl-test-vm"
            }));
            Assert.Contains("inside the configured image storage", ex.Message);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public void ResolveTemplatePath_ThrowsWhenRequestedTemplateIsMissing()
    {
        using var tempRoot = new TempDirectory();
        var service = CreateKvmService(tempRoot.Path);

        var ex = Assert.Throws<FileNotFoundException>(() => service.ResolveTemplatePath(new CreateVmRequest
        {
            TemplateId = 115,
            VmName = "tl-test-vm"
        }));

        Assert.Contains("VM template image was not found", ex.Message);
    }

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
    public async Task GetStatusAsync_ReturnsVersionsAndToolCapabilities()
    {
        var service = CreateService(enable: false);

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(status.AgentVersion));
        Assert.True(status.ProtocolVersion >= 2);
        Assert.Equal(status.HasDockerCommand, status.Capabilities.Docker);
        Assert.Equal(status.HasKvmCommand, status.Capabilities.Kvm);
        Assert.Equal(status.HasWireGuardCommand, status.Capabilities.WireGuard);
        Assert.Equal(status.HasIptablesCommand, status.Capabilities.Iptables);
        Assert.Equal(status.HasNftCommand, status.Capabilities.Nftables);
        Assert.Equal(status.HasTcpdumpCommand, status.Capabilities.Tcpdump);
        Assert.Equal(status.HasDumpcapCommand, status.Capabilities.Dumpcap);
        Assert.Equal(File.Exists("/dev/kvm"), status.Capabilities.KvmDevice);
    }

    [Fact]
    public async Task StartFlowMetadataAsync_DryRunCreatesScopedTcpdumpCollector()
    {
        var service = CreateService(enable: false);

        var result = await service.StartFlowMetadataAsync(new TeamLabFlowStartRequest(
            RuntimeId: 123,
            ShardId: 45,
            NetworkId: 67,
            NetworkKey: "entry_zone",
            InterfaceName: "tl123-entry",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.True(result.DryRun);
        Assert.Contains(result.Commands, command =>
            command.Contains("/run/gzctf-teamlab/flow-123-entry_zone", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command =>
            command.Contains("tcpdump -l -tttt -nn -q -i 'tl123-entry' ip", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command =>
            command.Contains("flow.pid", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command =>
            command.Contains("tcpdump", StringComparison.Ordinal) &&
            command.Contains("echo $! > '/run/gzctf-teamlab/flow-123-entry_zone/flow.pid'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartFlowMetadataAsync_RejectsUnsafeNetworkKey()
    {
        var service = CreateService(enable: false);

        var result = await service.StartFlowMetadataAsync(new TeamLabFlowStartRequest(
            RuntimeId: 123,
            ShardId: null,
            NetworkId: null,
            NetworkKey: "entry;rm",
            InterfaceName: "tl123-entry",
            DryRun: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public void TryParseTcpdumpFlowLine_ParsesTcpUdpAndIcmpSamples()
    {
        Assert.True(TeamLabNetworkService.TryParseTcpdumpFlowLine(
            "2026-07-07 10:11:12.123456 IP 10.180.33.10.43122 > 192.168.80.10.80: tcp 0, length 1460",
            out var tcp));
        Assert.Equal("TCP", tcp.Protocol);
        Assert.Equal("10.180.33.10", tcp.SourceIp);
        Assert.Equal(43122, tcp.SourcePort);
        Assert.Equal("192.168.80.10", tcp.DestinationIp);
        Assert.Equal(80, tcp.DestinationPort);
        Assert.Equal(1460, tcp.Bytes);

        Assert.True(TeamLabNetworkService.TryParseTcpdumpFlowLine(
            "2026-07-07 10:11:13.123456 IP 10.180.33.10 > 192.168.80.10: ICMP echo request, id 1, seq 2, length 64",
            out var icmp));
        Assert.Equal("ICMP", icmp.Protocol);
        Assert.Null(icmp.SourcePort);
        Assert.Equal(64, icmp.Bytes);
    }

    [Fact]
    public async Task CreateBridgeAsync_DryRunDeletesExistingBridgeBeforeRecreate()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateBridgeAsync(new TeamLabBridgeRequest(
            RuntimeId: 123,
            BridgeName: "tl123-dmz",
            Cidr: "10.180.1.0/24",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Collection(result.Commands.Take(2),
            command => Assert.Contains("ip link delete tl123-dmz 2>/dev/null || true", command),
            command => Assert.Contains("ip link add tl123-dmz type bridge", command));
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
    public async Task CreateRouterAsync_DryRunRecreatesNamespaceAndFlushesInterfaceAddresses()
    {
        var service = CreateService(enable: false);

        var result = await service.CreateRouterAsync(new TeamLabRouterRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            Interfaces:
            [
                new TeamLabRouterInterfaceRequest("tl123-entry", "10.180.1.1/24")
            ],
            Routes: [],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Collection(result.Commands.Take(3),
            command => Assert.Contains("ip netns pids tlr123 2>/dev/null | xargs -r kill 2>/dev/null || true", command),
            command => Assert.Contains("ip netns delete tlr123 2>/dev/null || true", command),
            command => Assert.Contains("ip netns add tlr123", command));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr flush dev tlr123n0"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr add 10.180.1.1/24 dev tlr123n0"));
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
    public async Task ApplyFabricAsync_DryRunBuildsNamespaceUplinkAndRoutes()
    {
        var service = CreateService(enable: false);

        var result = await service.ApplyFabricAsync(new TeamLabFabricApplyRequest(
            RuntimeId: 123,
            RouteVersion: 1,
            FabricIp: "10.24.0.31",
            NamespaceName: "tlr123",
            NamespaceHostAddressCidr: "169.254.123.1/30",
            NamespacePeerAddressCidr: "169.254.123.2/30",
            LocalRoutes:
            [
                new TeamLabStaticRouteRequest("10.77.10.0/24", "169.254.123.2")
            ],
            Routes:
            [
                new TeamLabStaticRouteRequest("10.180.53.48/28", "10.24.0.27", "10.77.10.1")
            ],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands, command => command.Contains("ip link add tlrf123 type veth peer name tlrf123n"));
        Assert.Contains(result.Commands, command => command.Contains("ip link set tlrf123n netns tlr123"));
        Assert.Contains(result.Commands, command => command.Contains("ip addr add 169.254.123.1/30 dev tlrf123"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr add 169.254.123.2/30 dev tlrf123n"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip route replace 10.77.10.0/24 via 169.254.123.2"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip route replace 10.180.53.48/28 via 169.254.123.1") &&
                       command.Contains("src 10.77.10.1"));
        Assert.Contains(result.Commands, command => command.Contains("iptables -N TEAMLAB-FABRIC"));
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -C FORWARD -j TEAMLAB-FABRIC") &&
                       command.Contains("iptables -I FORWARD 1 -j TEAMLAB-FABRIC"));
        Assert.Contains(result.Commands,
            command => command.Contains("--comment gzctf-teamlab-runtime-123") &&
                       command.Contains("-i tlrf123 -d 10.180.53.48/28 -j ACCEPT"));
        Assert.Contains(result.Commands,
            command => command.Contains("--comment gzctf-teamlab-runtime-123") &&
                       command.Contains("-o tlrf123 -s 10.77.10.0/24 -j ACCEPT"));
    }

    [Fact]
    public async Task CleanupAsync_DryRunRemovesRuntimeFabricForwardRules()
    {
        var service = CreateService(enable: false);

        var result = await service.CleanupAsync(new TeamLabCleanupRequest(
            RuntimeId: 123,
            ResourceNames: ["tlr123", "tlrf123"],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -S TEAMLAB-FABRIC") &&
                       command.Contains("--comment gzctf-teamlab-runtime-123") &&
                       command.Contains("-D TEAMLAB-FABRIC"));
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
            PeerAllowedIps: "10.60.0.0/28",
            PlayerAllowedCidrs: ["10.60.0.0/28"],
            PlayerBlockedCidrs: ["10.60.0.16/28"],
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
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -I FORWARD 1 -i tlwg123 -d 10.60.0.0/28 -j ACCEPT"));
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -A FORWARD -i tlwg123 -d 10.60.0.16/28 -j REJECT"));
        Assert.DoesNotContain(result.Commands, command => command.StartsWith("wg set ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Commands, command => command.Contains(ValidInterfacePrivateKey));
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_DryRunDeletesExistingInterfacesAndFlushesAddressBeforeUp()
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
            PeerAllowedIps: "10.250.0.2/32",
            PlayerAllowedCidrs: ["10.60.0.0/28"],
            PlayerBlockedCidrs: [],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Collection(result.Commands.Take(4),
            command => Assert.Contains("printf '<redacted>'", command),
            command => Assert.Contains("ip netns exec tlr123 ip link delete tlwg123 2>/dev/null || true", command),
            command => Assert.Contains("ip link delete tlwg123 2>/dev/null || true", command),
            command => Assert.Contains("ip link add tlwg123 type wireguard", command));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr flush dev tlwg123"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr add 10.250.0.10/32 dev tlwg123"));
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
            PlayerAllowedCidrs: [],
            PlayerBlockedCidrs: [],
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
        Assert.DoesNotContain(result.Commands, command => command.Contains("/etc/resolv.conf"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("nameserver 10.180.1.1"));
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
        Assert.Contains(result.Commands, command => command.Contains("--user=root --group=root"));
        Assert.Contains(result.Commands, command => command.Contains("--interface=tlr123n0"));
        Assert.Contains(result.Commands, command => command.Contains("--dhcp-range=10.180.1.1,static,255.255.255.0"));
        Assert.Contains(result.Commands, command => command.Contains("--dhcp-leasefile=/run/gzctf-teamlab/tldns123/leases"));
        Assert.Contains(result.Commands, command => command.Contains("02:42:ac:10:00:02,10.180.1.10,portal"));
        Assert.Contains(result.Commands, command => command.Contains("address=/portal.team123.lab/10.180.1.10"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("virbr0"));
    }

    [Fact]
    public void ParseIpFromDhcpLeases_ReadsDnsmasqBareLeaseAddress()
    {
        var output = "1783238118 02:42:99:18:00:10 10.199.0.10 vm-core 01:02:42:99:18:00:10";

        var ip = KvmService.ParseIpFromDhcpLeases(output, "02:42:99:18:00:10");

        Assert.Equal("10.199.0.10", ip);
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
            command => command.Contains("for i in $(seq 1 10)"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 nslookup portal.team123.lab 10.180.1.1"));
        Assert.Contains(result.Commands,
            command => command.Contains("sleep 1"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("nc -uz"));
    }

    [Fact]
    public async Task ApplyFabricAsync_DryRunBuildsDeterministicRouteReplaceCommands()
    {
        var service = CreateService(enable: false);

        var result = await service.ApplyFabricAsync(new TeamLabFabricApplyRequest(
            RuntimeId: 123,
            RouteVersion: 7,
            FabricIp: "10.250.0.10",
            Routes:
            [
                new TeamLabStaticRouteRequest("192.168.50.0/24", "10.250.0.12"),
                new TeamLabStaticRouteRequest("10.66.0.0/24", "10.250.0.11")
            ],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Equal(
        [
            "ip route replace 10.66.0.0/24 via 10.250.0.11",
            "ip route replace 192.168.50.0/24 via 10.250.0.12"
        ], result.Commands);
    }

    [Fact]
    public async Task ApplyFabricAsync_RejectsInvalidRouteTarget()
    {
        var service = CreateService(enable: false);

        var result = await service.ApplyFabricAsync(new TeamLabFabricApplyRequest(
            RuntimeId: 123,
            RouteVersion: 7,
            FabricIp: "10.250.0.10",
            Routes: [new TeamLabStaticRouteRequest("not-a-cidr", "10.250.0.12")],
            DryRun: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Message);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task StartCaptureAsync_DryRunUsesRuntimeJobScopedDirectory()
    {
        var service = CreateService(enable: false);

        var result = await service.StartCaptureAsync(new TeamLabCaptureStartRequest(
            RuntimeId: 123,
            JobId: 456,
            Scope: "network:entry",
            InterfaceName: "tl123-entry",
            MaxSeconds: 300,
            MaxBytes: 64 * 1024 * 1024,
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Equal("/run/gzctf-teamlab/capture-123-456/capture.pcap", result.FilePath);
        Assert.Contains(result.Commands,
            command => command.Contains("mkdir -p '/run/gzctf-teamlab/capture-123-456'"));
        Assert.Contains(result.Commands,
            command => command.Contains("-i 'tl123-entry'"));
        Assert.Contains(result.Commands,
            command => command.Contains("/run/gzctf-teamlab/capture-123-456/capture.pcap"));
    }

    [Fact]
    public async Task StartCaptureAsync_RejectsUnsafeInterfaceName()
    {
        var service = CreateService(enable: false);

        var result = await service.StartCaptureAsync(new TeamLabCaptureStartRequest(
            RuntimeId: 123,
            JobId: 456,
            Scope: "network:entry",
            InterfaceName: "eth0;rm",
            MaxSeconds: 300,
            MaxBytes: 64 * 1024 * 1024,
            DryRun: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Message);
        Assert.Null(result.FilePath);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task StopCaptureAsync_DryRunKillsOnlyRuntimeJobScopedPidFile()
    {
        var service = CreateService(enable: false);

        var result = await service.StopCaptureAsync(new TeamLabCaptureStopRequest(
            RuntimeId: 123,
            JobId: 456,
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Equal("/run/gzctf-teamlab/capture-123-456/capture.pcap", result.FilePath);
        Assert.Contains(result.Commands,
            command => command.Contains("/run/gzctf-teamlab/capture-123-456/capture.pid"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("killall"));
    }

    [Fact]
    public void ResolveCaptureFilePath_UsesOnlyRuntimeAndJobScopedDirectory()
    {
        var path = TeamLabNetworkService.ResolveCaptureFilePath(123, 456);

        Assert.Equal("/run/gzctf-teamlab/capture-123-456/capture.pcap", path);
    }

    [Fact]
    public void ResolveCaptureFilePath_RejectsInvalidRuntimeOrJobIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TeamLabNetworkService.ResolveCaptureFilePath(0, 456));
        Assert.Throws<ArgumentOutOfRangeException>(() => TeamLabNetworkService.ResolveCaptureFilePath(123, 0));
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
            PlayerAllowedCidrs: ["10.180.1.0/28"],
            PlayerBlockedCidrs: [],
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

    private static KvmService CreateKvmService(string imageStoragePath) => new(
        Options.Create(new KvmConfig { ImageStoragePath = imageStoragePath }),
        NullLogger<KvmService>.Instance);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gzctf-test-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

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
