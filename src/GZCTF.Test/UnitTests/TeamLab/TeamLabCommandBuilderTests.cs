using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Observation;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Agent.Services.Vm;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabCommandBuilderTests
{
    private const string ValidInterfacePrivateKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string ValidPeerPublicKey = "ISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0A=";

    [Fact]
    public void DockerGateCommand_PreservesImageEntrypointAndCmdWithoutStartCommand()
    {
        var command = DockerService.BuildGatedCommand(
            ["/usr/bin/original", "--mode", "worker"],
            ["serve", "--port", "8080"],
            startCommand: null);

        Assert.Equal(["/usr/bin/original", "--mode", "worker", "serve", "--port", "8080"], command.Command);
        Assert.Equal("sh", command.Entrypoint[0]);
        Assert.Contains(".gzctf-teamlab-network-ready", command.Entrypoint[2], StringComparison.Ordinal);
        Assert.DoesNotContain("sleep 0.2", command.Entrypoint[2], StringComparison.Ordinal);
    }

    [Fact]
    public void DockerGateCommand_PreservesImageEntrypointWhenStartCommandOverridesCmd()
    {
        var command = DockerService.BuildGatedCommand(
            ["/usr/bin/original", "--mode", "worker"],
            ["image-default"],
            "custom --flag");

        Assert.Equal(
            ["/usr/bin/original", "--mode", "worker", "sh", "-c", "custom --flag"],
            command.Command);
    }

    [Fact]
    public void LinuxResourceNames_RemainBoundedAndDoNotCollideOnLongSharedPrefixes()
    {
        var first = TeamLabResourceNameFactory.LinuxName("tl42-windows-domain-controller-primary");
        var second = TeamLabResourceNameFactory.LinuxName("tl42-windows-domain-controller-secondary");

        Assert.True(first.Length <= 15);
        Assert.True(second.Length <= 15);
        Assert.NotEqual(first, second);
        Assert.Equal(first, TeamLabResourceNameFactory.LinuxName("tl42-windows-domain-controller-primary"));
    }

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
        var service = CreateBridgeService(enable: false);

        var result = await service.ApplyAsync(new TeamLabBridgeRequest(
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
    public async Task GetStatusAsync_ReturnsToolCapabilities()
    {
        var service = CreateService(enable: false);

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(status.HasDockerCommand, status.Capabilities.Docker);
        Assert.Equal(status.HasKvmCommand, status.Capabilities.Kvm);
        Assert.Equal(status.HasWireGuardCommand, status.Capabilities.WireGuard);
        Assert.Equal(status.HasIptablesCommand, status.Capabilities.Iptables);
        Assert.Equal(status.HasNftCommand, status.Capabilities.Nftables);
        Assert.Equal(status.HasTcpdumpCommand, status.Capabilities.Tcpdump);
        Assert.Equal(status.HasDumpcapCommand, status.Capabilities.Dumpcap);
        Assert.Equal(
            new[] { "/sbin", "/usr/sbin", "/bin", "/usr/bin", "/usr/local/bin" }
                .Any(path => File.Exists(Path.Combine(path, "dig"))),
            status.Capabilities.DnsProbe);
        Assert.Equal(File.Exists("/dev/kvm"), status.Capabilities.KvmDevice);
    }

    [Fact]
    public async Task ApplyInfrastructureAsync_ReturnsStableDesiredStateDigestAndNativeFacts()
    {
        var service = CreateService(enable: false);
        var request = InfrastructureRequest(runtimeId: 123, dryRun: true);

        var first = await service.ApplyInfrastructureAsync(request, CancellationToken.None);
        var second = await service.ApplyInfrastructureAsync(request, CancellationToken.None);

        Assert.True(first.Success, first.Message);
        Assert.True(first.DryRun);
        Assert.Equal(first.DesiredStateDigest, second.DesiredStateDigest);
        Assert.StartsWith("sha256:", first.DesiredStateDigest, StringComparison.Ordinal);
        Assert.Contains(first.Resources, item =>
            item.Kind == "managed-switch" && item.Key == "entry" && item.NativeIdentity == "tl123-entry");
        Assert.Contains(first.Resources, item =>
            item.Kind == "managed-router-fragment" && item.Key == "router" && item.NativeIdentity == "tlr123");
        Assert.Contains(first.Resources, item =>
            item.Kind == "fabric-uplink" && item.NativeIdentity == "tlrf123");
    }

    [Fact]
    public async Task ApplyInfrastructureAsync_MatchingDigestReturnsAlreadyAppliedWhenLiveStateIsHealthy()
    {
        var runtimeId = 900000 + Random.Shared.Next(1, 90000);
        var request = InfrastructureRequest(runtimeId, dryRun: true);
        var planned = await CreateService(enable: false)
            .ApplyInfrastructureAsync(request, CancellationToken.None);
        var statePath = TeamLabNetworkService.ResolveDesiredStatePath(runtimeId, request.Generation);
        var runtimeDirectory = Path.GetDirectoryName(Path.GetDirectoryName(statePath))!;
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        try
        {
            File.WriteAllText(statePath, JsonSerializer.Serialize(new
            {
                RuntimeId = runtimeId,
                Generation = request.Generation,
                RouteVersion = request.RouteVersion,
                DesiredStateDigest = planned.DesiredStateDigest,
                Resources = Array.Empty<object>(),
                AppliedAt = DateTimeOffset.UtcNow
            }));
            var runner = new InfrastructureStateTeamLabCommandRunner(probeHealthy: true);
            var service = CreateService(enable: true, runner, dryRun: false);

            var result = await service.ApplyInfrastructureAsync(
                request with { DryRun = false }, CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.True(result.AlreadyApplied);
            Assert.False(result.DryRun);
            Assert.Empty(result.Commands);
            Assert.Equal(planned.DesiredStateDigest, result.DesiredStateDigest);
            Assert.Contains(runner.Commands, command =>
                command.Contains("ip netns exec tlr123 ip link show tlr123n0", StringComparison.Ordinal));
            Assert.DoesNotContain(runner.Commands, command =>
                command.Contains("ip netns delete tlr123", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, true);
        }
    }

    [Fact]
    public async Task ApplyInfrastructureAsync_MatchingDigestReconcilesWhenLiveStateHasDrifted()
    {
        var runtimeId = 900000 + Random.Shared.Next(1, 90000);
        var request = InfrastructureRequest(runtimeId, dryRun: true);
        var planned = await CreateService(enable: false)
            .ApplyInfrastructureAsync(request, CancellationToken.None);
        var statePath = TeamLabNetworkService.ResolveDesiredStatePath(runtimeId, request.Generation);
        var runtimeDirectory = Path.GetDirectoryName(Path.GetDirectoryName(statePath))!;
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        try
        {
            File.WriteAllText(statePath, JsonSerializer.Serialize(new
            {
                RuntimeId = runtimeId,
                Generation = request.Generation,
                RouteVersion = request.RouteVersion,
                DesiredStateDigest = planned.DesiredStateDigest,
                Resources = Array.Empty<object>(),
                AppliedAt = DateTimeOffset.UtcNow
            }));
            var runner = new InfrastructureStateTeamLabCommandRunner(probeHealthy: false);
            var service = CreateService(enable: true, runner, dryRun: false);

            var result = await service.ApplyInfrastructureAsync(
                request with { DryRun = false }, CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.False(result.AlreadyApplied);
            Assert.DoesNotContain(runner.Commands, command =>
                command.Contains("ip netns delete tlr123", StringComparison.Ordinal));
            Assert.Contains(runner.Commands, command =>
                command.Contains("host_link", StringComparison.Ordinal) &&
                command.Contains("peer_index", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, true);
        }
    }

    [Fact]
    public void InfrastructureFactProbe_ChecksRouterDnsmasqFabricRoutesAndFirewallChains()
    {
        var request = InfrastructureRequest(runtimeId: 123, dryRun: false) with
        {
            Fabric = InfrastructureRequest(runtimeId: 123, dryRun: false).Fabric with
            {
                RemoteRoutes = [new TeamLabStaticRouteRequest("192.168.50.0/24", "10.24.0.32")]
            },
            ForwardPolicies =
            [
                new TeamLabForwardPolicyRequest("10.10.0.0/24", "192.168.50.0/24", true),
                new TeamLabForwardPolicyRequest("192.168.50.0/24", "10.10.0.0/24", false)
            ]
        };

        var command = TeamLabNetworkService.BuildInfrastructureFactProbeCommand(request);

        Assert.Contains("ip link show tl123-entry", command, StringComparison.Ordinal);
        Assert.Contains("ip link show tlr123h0", command, StringComparison.Ordinal);
        Assert.Contains("ip netns exec tlr123 ip link show tlr123n0", command, StringComparison.Ordinal);
        Assert.Contains("--interface=tlr123n0", command, StringComparison.Ordinal);
        Assert.Contains("dnsmasq.pid", command, StringComparison.Ordinal);
        Assert.Contains("ss -H -lunp", command, StringComparison.Ordinal);
        Assert.Contains("ip route show exact 10.10.0.0/24", command, StringComparison.Ordinal);
        Assert.Contains("ip route show exact 192.168.50.0/24", command, StringComparison.Ordinal);
        Assert.Contains("TLR7BG3", command, StringComparison.Ordinal);
        Assert.Contains("TLA7BG3", command, StringComparison.Ordinal);
        Assert.Contains("TLI7BG3", command, StringComparison.Ordinal);
        Assert.Contains("TLM7BG3", command, StringComparison.Ordinal);
        Assert.Contains("TLF7BG3", command, StringComparison.Ordinal);
        Assert.Contains("policy drop", command, StringComparison.Ordinal);
        Assert.Contains("ESTABLISHED,RELATED", command, StringComparison.Ordinal);
        Assert.Contains("--set-mss 1380", command, StringComparison.Ordinal);
        Assert.Contains("10.10.0.0/24", command, StringComparison.Ordinal);
        Assert.Contains("192.168.50.0/24", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyInfrastructureAsync_DnsmasqReadinessFailureDoesNotWriteDesiredState()
    {
        var runtimeId = 900000 + Random.Shared.Next(1, 90000);
        var request = InfrastructureRequest(runtimeId, dryRun: false);
        var statePath = TeamLabNetworkService.ResolveDesiredStatePath(runtimeId, request.Generation);
        var runtimeDirectory = Path.GetDirectoryName(Path.GetDirectoryName(statePath))!;
        if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, true);
        try
        {
            var service = CreateService(
                enable: true,
                new DnsmasqFailureTeamLabCommandRunner(),
                dryRun: false);

            var result = await service.ApplyInfrastructureAsync(request, CancellationToken.None);

            Assert.False(result.Success);
            Assert.False(File.Exists(statePath));
            Assert.Contains("dnsmasq did not become ready", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, true);
        }
    }

    [Fact]
    public async Task CreateBridgeAsync_DryRunEnsuresBridgeWithoutDestructiveRecreate()
    {
        var service = CreateBridgeService(enable: false);

        var result = await service.ApplyAsync(new TeamLabBridgeRequest(
            RuntimeId: 123,
            BridgeName: "tl123-dmz",
            Cidr: "10.180.1.0/24",
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands,
            command => command.Contains("ip link show tl123-dmz") &&
                       command.Contains("ip link add tl123-dmz type bridge"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("ip link delete tl123-dmz"));
    }

    [Fact]
    public async Task CreateBridgeAsync_RejectsUnsafeLinuxResourceName()
    {
        var service = CreateBridgeService(enable: false);

        var result = await service.ApplyAsync(new TeamLabBridgeRequest(
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
        var service = CreateRouterService(enable: false);

        var result = await service.ApplyAsync(new TeamLabRouterRequest(
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
    public async Task CreateRouterAsync_DryRunConvergesNamespaceWithoutDestroyingLiveProcesses()
    {
        var service = CreateRouterService(enable: false);

        var result = await service.ApplyAsync(new TeamLabRouterRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            Interfaces:
            [
                new TeamLabRouterInterfaceRequest("tl123-entry", "10.180.1.1/24")
            ],
            Routes: [],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands,
            command => command.Contains("grep -Fx 'tlr123'", StringComparison.Ordinal) &&
                       command.Contains("ip netns add tlr123", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Commands,
            command => command.Contains("ip netns pids", StringComparison.Ordinal) ||
                       command.Contains("ip netns delete", StringComparison.Ordinal));
        Assert.Contains(result.Commands,
            command => command.Contains("host_link", StringComparison.Ordinal) &&
                       command.Contains("peer_index", StringComparison.Ordinal));
        Assert.Contains(result.Commands,
            command => command.Contains("alias 'gzctf-teamlab-router:123'", StringComparison.Ordinal));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr flush dev tlr123n0"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr add 10.180.1.1/24 dev tlr123n0"));
    }

    [Fact]
    public async Task CreateRouterAsync_DryRunConfiguresStaticRoutesInsideNamespace()
    {
        var service = CreateRouterService(enable: false);

        var result = await service.ApplyAsync(new TeamLabRouterRequest(
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
        var service = CreateFabricService(enable: false);

        var result = await service.ApplyAsync(new TeamLabFabricApplyRequest(
            RuntimeId: 123,
            Generation: 1,
            RouteVersion: 1,
            FabricIp: "10.24.0.31",
            NamespaceName: "tlr123",
            NamespaceHostAddressCidr: "169.254.123.1/30",
            NamespacePeerAddressCidr: "169.254.123.2/30",
            HostInterfaceName: "tlrf123",
            NamespaceInterfaceName: "tlrf123n",
            LocalRoutes:
            [
                new TeamLabStaticRouteRequest("10.77.10.0/24", "169.254.123.2")
            ],
            Routes:
            [
                new TeamLabStaticRouteRequest("10.180.53.48/28", "10.24.0.27", "10.77.10.1")
            ],
            ForwardPolicies:
            [
                new TeamLabForwardPolicyRequest("10.77.10.0/24", "10.180.53.48/28", true),
                new TeamLabForwardPolicyRequest("10.77.10.0/24", "192.168.50.0/24", false)
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
        Assert.Contains(result.Commands, command => command.Contains("iptables -N TLF7BG1"));
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -C FORWARD -j TLF7BG1") &&
                       command.Contains("iptables -I FORWARD 1 -j TLF7BG1"));
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -A TLF7BG1") &&
                       command.Contains("-i tlrf123 -d 10.180.53.48/28 -j ACCEPT"));
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -A TLF7BG1") &&
                       command.Contains("-o tlrf123 -s 10.77.10.0/24 -j ACCEPT"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 iptables -A TLR7BG1") &&
                       command.Contains("-s 10.77.10.0/24 -d 10.180.53.48/28 -j ACCEPT"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 iptables -A TLR7BG1") &&
                       command.Contains("-s 10.77.10.0/24 -d 192.168.50.0/24 -j REJECT"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 iptables -A TLR7BG1") &&
                       command.Contains("ESTABLISHED,RELATED -j ACCEPT"));
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -t mangle -A TLM7BG1") &&
                       command.Contains("-o tlrf123n") && command.Contains("--set-mss 1380"));
    }

    [Fact]
    public async Task FabricPeerRoutes_AddRemoteCidrsToOwningWireGuardPeer()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"gzctf-fabric-{Guid.NewGuid():N}");
        var options = Options.Create(new AgentTeamLabConfig
        {
            Enable = true,
            DryRun = false,
            FabricInterfaceName = "gzctf-fabric",
            RuntimeStateRoot = stateRoot
        });
        var runner = new FabricPeerTeamLabCommandRunner();
        var executor = new TeamLabCommandExecutor(options, runner, NullLogger<TeamLabCommandExecutor>.Instance);
        var service = new TeamLabFabricService(
            executor,
            new TeamLabFirewallService(executor, options),
            runner,
            new TeamLabFabricRouteStore(options),
            options,
            NullLogger<TeamLabFabricService>.Instance);

        try
        {
            var result = await service.EnsurePeerRoutesAsync(new TeamLabFabricApplyRequest(
                RuntimeId: 123,
                Generation: 1,
                RouteVersion: 1,
                FabricIp: "10.250.0.1",
                Routes: [new TeamLabStaticRouteRequest("172.23.0.0/24", "10.250.0.2")],
                DryRun: false), CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains(runner.Commands, command =>
                command.StartsWith("wg set ", StringComparison.Ordinal) &&
                command.Contains("allowed-ips '10.250.0.2/32,172.23.0.0/24'"));
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, true);
        }
    }

    [Fact]
    public async Task FabricPeerRoutes_CleanupPreservesCidrsClaimedByAnotherRuntime()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"gzctf-fabric-{Guid.NewGuid():N}");
        var options = Options.Create(new AgentTeamLabConfig
        {
            Enable = true,
            DryRun = false,
            FabricInterfaceName = "gzctf-fabric",
            RuntimeStateRoot = stateRoot
        });
        var runner = new FabricPeerTeamLabCommandRunner();
        var executor = new TeamLabCommandExecutor(options, runner, NullLogger<TeamLabCommandExecutor>.Instance);
        var service = new TeamLabFabricService(
            executor,
            new TeamLabFirewallService(executor, options),
            runner,
            new TeamLabFabricRouteStore(options),
            options,
            NullLogger<TeamLabFabricService>.Instance);
        var route = new TeamLabStaticRouteRequest("172.23.0.0/24", "10.250.0.2");

        try
        {
            foreach (var runtimeId in new[] { 123, 124 })
            {
                var apply = await service.EnsurePeerRoutesAsync(new TeamLabFabricApplyRequest(
                    runtimeId,
                    Generation: 1,
                    RouteVersion: 1,
                    FabricIp: "10.250.0.1",
                    Routes: [route],
                    DryRun: false), CancellationToken.None);
                Assert.True(apply.Success);
            }

            var firstCleanup = await service.RemovePeerRoutesAsync(
                123, 1, [route.TargetCidr], false, CancellationToken.None);
            Assert.True(firstCleanup.Success);
            Assert.Contains(route.TargetCidr, runner.AllowedIps);

            var secondCleanup = await service.RemovePeerRoutesAsync(
                124, 1, [route.TargetCidr], false, CancellationToken.None);
            Assert.True(secondCleanup.Success);
            Assert.DoesNotContain(route.TargetCidr, runner.AllowedIps);
            Assert.Contains("10.250.0.2/32", runner.AllowedIps);
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, true);
        }
    }

    [Fact]
    public async Task FabricPeerRoutes_RepeatedApplyDoesNotRewriteMatchingAllowedIps()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"gzctf-fabric-{Guid.NewGuid():N}");
        var options = Options.Create(new AgentTeamLabConfig
        {
            Enable = true,
            DryRun = false,
            FabricInterfaceName = "gzctf-fabric",
            RuntimeStateRoot = stateRoot
        });
        var runner = new FabricPeerTeamLabCommandRunner();
        var executor = new TeamLabCommandExecutor(options, runner, NullLogger<TeamLabCommandExecutor>.Instance);
        var service = new TeamLabFabricService(
            executor,
            new TeamLabFirewallService(executor, options),
            runner,
            new TeamLabFabricRouteStore(options),
            options,
            NullLogger<TeamLabFabricService>.Instance);
        var request = new TeamLabFabricApplyRequest(
            123,
            Generation: 1,
            RouteVersion: 1,
            FabricIp: "10.250.0.1",
            Routes: [new TeamLabStaticRouteRequest("172.23.0.0/24", "10.250.0.2")],
            DryRun: false);

        try
        {
            Assert.True((await service.EnsurePeerRoutesAsync(request, CancellationToken.None)).Success);
            Assert.True((await service.EnsurePeerRoutesAsync(request, CancellationToken.None)).Success);
            Assert.Single(runner.Commands, command => command.StartsWith("wg set ", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, true);
        }
    }

    [Fact]
    public async Task FabricPeerRoutes_ReconcileRestoresPersistedAuthoritativeState()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"gzctf-fabric-{Guid.NewGuid():N}");
        var options = Options.Create(new AgentTeamLabConfig
        {
            Enable = true,
            DryRun = false,
            FabricInterfaceName = "gzctf-fabric",
            RuntimeStateRoot = stateRoot
        });
        var runner = new FabricPeerTeamLabCommandRunner();
        var executor = new TeamLabCommandExecutor(options, runner, NullLogger<TeamLabCommandExecutor>.Instance);
        TeamLabFabricService CreateFabric() => new(
            executor,
            new TeamLabFirewallService(executor, options),
            runner,
            new TeamLabFabricRouteStore(options),
            options,
            NullLogger<TeamLabFabricService>.Instance);

        try
        {
            var apply = await CreateFabric().EnsurePeerRoutesAsync(new TeamLabFabricApplyRequest(
                123,
                Generation: 1,
                RouteVersion: 1,
                FabricIp: "10.250.0.1",
                Routes: [new TeamLabStaticRouteRequest("172.23.0.0/24", "10.250.0.2")],
                DryRun: false), CancellationToken.None);
            Assert.True(apply.Success);

            runner.ReplaceAllowedIps(["10.250.0.2/32"]);
            var reconcile = await CreateFabric().ReconcilePeerRoutesAsync(CancellationToken.None);

            Assert.True(reconcile.Success);
            Assert.Contains("172.23.0.0/24", runner.AllowedIps);
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, true);
        }
    }

    [Fact]
    public async Task CleanupAsync_DryRunRemovesRuntimeFabricForwardRules()
    {
        var service = CreateService(enable: false);

        var result = await service.CleanupAsync(new TeamLabCleanupRequest(
            RuntimeId: 123,
            Generation: 1,
            RouterNamespace: "tlr123",
            ResourceNames: ["tlr123", "tlrf123"],
            SensorAssetKeys: [],
            FabricRemoteCidrs: ["10.77.10.0/24"],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -D FORWARD -j TLF7BG1"));
        Assert.Contains(result.Commands,
             command => command.Contains("/run/gzctf-teamlab/runtime-123/generation-1"));
    }

    [Fact]
    public async Task CleanupAsync_StaleGenerationDoesNotDeleteSharedRuntimeResources()
    {
        var runtimeId = 800000 + Random.Shared.Next(1, 100000);
        var activeStatePath = Path.Combine(
            "/var/lib/gzctf/teamlab",
            $"runtime-{runtimeId}",
            "active-generation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(activeStatePath)!);
        await File.WriteAllTextAsync(activeStatePath, JsonSerializer.Serialize(new
        {
            runtimeId,
            generation = 2
        }));

        try
        {
            var service = CreateService(enable: false);
            var result = await service.CleanupAsync(new TeamLabCleanupRequest(
                RuntimeId: runtimeId,
                Generation: 1,
                RouterNamespace: "tlr123",
                ResourceNames: ["tlr123", "tl123-entry", "tlrf123"],
                SensorAssetKeys: ["web"],
                FabricRemoteCidrs: ["10.77.10.0/24"],
                DryRun: true), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Contains(result.Commands, command =>
                command.Contains($"runtime-{runtimeId}/generation-1", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Commands, command =>
                command.Contains("ip link delete tl123-entry", StringComparison.Ordinal) ||
                command.Contains("ip netns delete tlr123", StringComparison.Ordinal) ||
                command.Contains("10.77.10.0/24", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(activeStatePath)!, recursive: true);
        }
    }

    [Fact]
    public async Task CleanupAsync_MissingActiveGenerationDoesNotInferSharedResourceOwnership()
    {
        var runtimeId = 900000 + Random.Shared.Next(1, 100000);
        var runner = new InfrastructureStateTeamLabCommandRunner(probeHealthy: true);
        var service = CreateService(enable: true, runner, dryRun: false);

        var result = await service.CleanupAsync(new TeamLabCleanupRequest(
            RuntimeId: runtimeId,
            Generation: 1,
            RouterNamespace: "tlr123",
            ResourceNames: ["tlr123", "tl123-entry", "tlrf123"],
            SensorAssetKeys: [],
            FabricRemoteCidrs: ["10.77.10.0/24"],
            DryRun: false), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(runner.Commands, command =>
            command.Contains("ip link delete tl123-entry", StringComparison.Ordinal) ||
            command.Contains("ip netns delete tlr123", StringComparison.Ordinal) ||
            command.Contains("10.77.10.0/24", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CleanupAsync_MissingActiveGenerationWithDesiredStateFailsClosed()
    {
        var runtimeId = 950000 + Random.Shared.Next(1, 40000);
        var statePath = TeamLabNetworkService.ResolveDesiredStatePath(runtimeId, 1);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        await File.WriteAllTextAsync(statePath, "{}");
        var runner = new InfrastructureStateTeamLabCommandRunner(probeHealthy: true);

        try
        {
            var service = CreateService(enable: true, runner, dryRun: false);
            var result = await service.CleanupAsync(new TeamLabCleanupRequest(
                RuntimeId: runtimeId,
                Generation: 1,
                RouterNamespace: "tlr123",
                ResourceNames: ["tlr123", "tl123-entry", "tlrf123"],
                SensorAssetKeys: [],
                FabricRemoteCidrs: ["10.77.10.0/24"],
                DryRun: false), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Active generation state is unavailable", result.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Commands);
            Assert.True(File.Exists(statePath));
        }
        finally
        {
            var runtimeDirectory = Directory.GetParent(Path.GetDirectoryName(statePath)!)!.FullName;
            if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, true);
        }
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_DryRunBuildsPeerCommand()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            Generation: 1,
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
            command => command.Contains("wg set tlwg123 private-key /dev/stdin"));
        Assert.Contains(result.Commands, command => command.Contains("listen-port 42001"));
        Assert.Contains(result.Commands, command => command.Contains("allowed-ips 10.250.0.2/32"));
        Assert.DoesNotContain(result.Commands, command => command.Contains("allowed-ips 10.60.0.0/28"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip route replace 10.250.0.2/32 dev tlwg123"));
        Assert.Contains(result.Commands,
            command => command.Contains("iptables -t nat -A TLNtlwg123 -s 10.250.0.2/32 -d 10.60.0.0/28 -j MASQUERADE"));
        Assert.DoesNotContain(result.Commands,
            command => command.Contains("ip route replace 10.60.0.0/28 dev tlwg123"));
        Assert.Contains(result.Commands,
            command => command.Contains(
                "iptables -A TLA7BG1 -i tlwg123 -s 10.250.0.2/32 -d 10.60.0.0/28 -j ACCEPT"));
        Assert.Contains(result.Commands,
            command => command.Contains(
                "iptables -A TLA7BG1 -i tlwg123 -s 10.250.0.2/32 -d 10.60.0.16/28 -j REJECT"));
        var configureCommand = Assert.Single(result.Commands,
            command => command.Contains("wg set tlwg123 private-key /dev/stdin", StringComparison.Ordinal));
        Assert.True(
            configureCommand.IndexOf("wg set tlwg123 private-key", StringComparison.Ordinal) <
            configureCommand.IndexOf("ip link set tlwg123 netns tlr123", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Commands, command => command.Contains(ValidInterfacePrivateKey));
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_DryRunUpdatesExistingInterfaceWithoutDeletingIt()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            Generation: 1,
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
        Assert.Contains(result.Commands, command => command.Contains("printf '<redacted>'", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command =>
            command.Contains("if ip netns exec tlr123 ip link show dev tlwg123", StringComparison.Ordinal) &&
            command.Contains("ip link add tlwg123 type wireguard", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Commands, command =>
            command.StartsWith("ip netns exec tlr123 ip link delete tlwg123", StringComparison.Ordinal));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr flush dev tlwg123"));
        Assert.Contains(result.Commands,
            command => command.Contains("ip netns exec tlr123 ip addr add 10.250.0.10/32 dev tlwg123"));
    }

    [Fact]
    public async Task ConfigureHostWireGuardAsync_BringsInterfaceUpBeforeAddingRoutes()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 196,
            Generation: 1,
            NamespaceName: "unused",
            InterfaceName: "tlwg196",
            ListenPort: 32001,
            AddressCidr: "10.1.1.254/32",
            InterfacePrivateKey: ValidInterfacePrivateKey,
            PeerPublicKey: ValidPeerPublicKey,
            PeerClientAddress: "10.1.1.2/32",
            PeerAllowedIps: "10.1.1.0/24",
            PlayerAllowedCidrs: ["10.1.1.0/24"],
            PlayerBlockedCidrs: [],
            DryRun: true,
            ExecutionModel: GZCTF.TeamLab.Contracts.TeamLabExecutionModel.V2,
            RuntimePublicId: Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001"),
            NetworkKey: "network",
            PortKey: "player-gateway",
            MacAddress: "02:42:ac:10:00:02"), CancellationToken.None);

        Assert.True(result.Success);
        var upIndex = Array.FindIndex(result.Commands, command => command.Contains("ip link set tlwg196 up", StringComparison.Ordinal));
        var routeIndex = Array.FindIndex(result.Commands, command => command.Contains("ip route replace 10.1.1.0/24 dev tlwg196", StringComparison.Ordinal));
        Assert.True(upIndex >= 0, "Expected a WireGuard interface up command.");
        Assert.True(routeIndex > upIndex, "WireGuard routes must be added after the interface is up.");
        Assert.DoesNotContain(result.Commands,
            command => command.Contains("ip link set tlwg198 address", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigureWireGuardAsync_RejectsPlaceholderKeys()
    {
        var service = CreateService(enable: false);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            Generation: 1,
            NamespaceName: "tlr123",
            InterfaceName: "tlwg123",
            ListenPort: 42001,
            AddressCidr: "10.250.0.10/32",
            InterfacePrivateKey: "test-peer-key",
            PeerPublicKey: "test-peer-key",
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
    public async Task ProbeAsync_DryRunBuildsNamespaceTcpProbe()
    {
        var service = CreateService(enable: false);

        var result = await service.ProbeAsync(new TeamLabProbeRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            TargetIp: "10.180.1.2",
            Kind: "TCP",
            Port: 443,
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands, command =>
            command.Contains("ip netns exec tlr123 timeout 3 bash -c", StringComparison.Ordinal) &&
            command.Contains("/dev/tcp/10.180.1.2/443", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_DryRunBuildsNamespaceHttpProbe()
    {
        var service = CreateService(enable: false);

        var result = await service.ProbeAsync(new TeamLabProbeRequest(
            RuntimeId: 123,
            NamespaceName: "tlr123",
            TargetIp: "10.180.1.2",
            Kind: "http",
            Port: 8080,
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Commands, command =>
            command.Contains("ip netns exec tlr123 curl --fail --silent --show-error", StringComparison.Ordinal) &&
            command.Contains("http://10.180.1.2:8080/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_RealTcpProbeFailureReturnsNonSuccess()
    {
        var service = CreateService(
            enable: true,
            new ProbeFailureTeamLabCommandRunner(),
            dryRun: false);

        var result = await service.ProbeAsync(new TeamLabProbeRequest(
            123, "tlr123", "10.180.1.2", "tcp", 443, false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.DryRun);
        Assert.Contains("connection refused", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("udp", 53)]
    [InlineData("tcp", null)]
    [InlineData("http", 0)]
    [InlineData("http", 65536)]
    public async Task ProbeAsync_RejectsInvalidKindOrPort(string kind, int? port)
    {
        var service = CreateService(enable: false);

        var result = await service.ProbeAsync(new TeamLabProbeRequest(
            123, "tlr123", "10.180.1.2", kind, port, true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public void FleetProbeRequest_OldDryRunConstructorRemainsCompatible()
    {
        var request = new GZCTF.Services.Fleet.TeamLabProbeRequest(
            123, "tlr123", "10.180.1.2", false);

        Assert.Null(request.Kind);
        Assert.Null(request.Port);
        Assert.False(request.DryRun);
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
    public void ContainerNetworkFinalizeCommand_VerifiesAllFactsAndDnsBeforeReleasingGate()
    {
        var request = new TeamLabContainerNetworkFinalizeRequest(
            OperationId: Guid.Parse("019f6b18-4acf-7d9f-a51a-2f4219893970"),
            RuntimeId: 123,
            Generation: 4,
            ContainerId: "abcdef123456",
            ContainerName: "gzctf_teamlab_web",
            Interfaces:
            [
                new TeamLabContainerInterfaceExpectation(
                    "eth0", "10.180.1.10/24", "02:42:ac:10:00:02")
            ],
            Routes:
            [
                new TeamLabContainerRouteExpectation("10.180.2.0/24", "10.180.1.1", "eth0")
            ],
            DnsServers: ["10.180.1.1"],
            DnsProbes:
            [
                new TeamLabContainerDnsProbeExpectation(
                    "10.180.1.1", "db.teamlab123.local", "10.180.1.20")
            ],
            RequireNoDefaultRoute: true,
            DryRun: false);

        var command = TeamLabContainerNetworkFinalizeService.BuildFinalizeCommand(4242, request);
        var releaseIndex = command.IndexOf("touch /proc/4242/root/tmp/.gzctf-teamlab-network-ready", StringComparison.Ordinal);

        Assert.Contains("ip link show dev 'eth0'", command, StringComparison.Ordinal);
        Assert.Contains("10.180.1.10/24", command, StringComparison.Ordinal);
        Assert.Contains("02:42:ac:10:00:02", command, StringComparison.Ordinal);
        Assert.Contains("ip route show exact '10.180.2.0/24'", command, StringComparison.Ordinal);
        Assert.Contains("nameserver 10.180.1.1", command, StringComparison.Ordinal);
        Assert.Contains("dig +time=2 +tries=1 +short @'10.180.1.1' 'db.teamlab123.local' A", command,
            StringComparison.Ordinal);
        Assert.True(releaseIndex > command.IndexOf("dig +time=2", StringComparison.Ordinal));
        Assert.DoesNotContain("sleep 0.2", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureDhcpDnsAsync_DryRunBuildsDnsmasqStaticLeaseCommands()
    {
        var service = CreateBridgeService(enable: false);

        var result = await service.ApplyDhcpDnsAsync(new TeamLabDhcpDnsRequest(
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
                new TeamLabDhcpLeaseRequest("02:42:ac:10:00:03", "10.180.1.20", "win-ad", false)
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
        Assert.Contains(result.Commands, command => command.Contains("--local=/team123.lab/"));
        Assert.Contains(result.Commands,
            command => command.Contains("02:42:ac:10:00:02,set:primary,10.180.1.10,portal"));
        Assert.Contains(result.Commands,
            command => command.Contains("02:42:ac:10:00:03,set:secondary,10.180.1.20,win-ad"));
        Assert.Contains(result.Commands, command => command.Contains("address=/portal.team123.lab/10.180.1.10"));
        Assert.Contains(result.Commands, command => command.Contains("dnsmasq.log", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command => command.Contains("kill -0", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command => command.Contains("ss -H -lunp", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command => command.Contains("dns_port_ready", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command => command.Contains("dhcp_port_ready", StringComparison.Ordinal));
        Assert.Contains(result.Commands, command => command.Contains("cat", StringComparison.Ordinal) &&
                                                   command.Contains("dnsmasq.log", StringComparison.Ordinal));
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
    public async Task ApplyFabricAsync_DryRunBuildsDeterministicRouteReplaceCommands()
    {
        var service = CreateFabricService(enable: false);

        var result = await service.ApplyAsync(new TeamLabFabricApplyRequest(
            RuntimeId: 123,
            Generation: 7,
            RouteVersion: 7,
            FabricIp: "10.250.0.10",
            NamespaceName: "tlr123",
            NamespaceHostAddressCidr: "169.254.123.1/30",
            NamespacePeerAddressCidr: "169.254.123.2/30",
            HostInterfaceName: "tlrf123",
            NamespaceInterfaceName: "tlrf123n",
            Routes:
            [
                new TeamLabStaticRouteRequest("192.168.50.0/24", "10.250.0.12"),
                new TeamLabStaticRouteRequest("10.66.0.0/24", "10.250.0.11")
            ],
            DryRun: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        var first = Array.FindIndex(result.Commands,
            command => command == "ip route replace 10.66.0.0/24 via 10.250.0.11 dev gzctf-fabric");
        var second = Array.FindIndex(result.Commands,
            command => command == "ip route replace 192.168.50.0/24 via 10.250.0.12 dev gzctf-fabric");
        Assert.True(first >= 0 && second > first);
    }

    [Fact]
    public async Task ApplyFabricAsync_RejectsInvalidRouteTarget()
    {
        var service = CreateFabricService(enable: false);

        var result = await service.ApplyAsync(new TeamLabFabricApplyRequest(
            RuntimeId: 123,
            Generation: 7,
            RouteVersion: 7,
            FabricIp: "10.250.0.10",
            NamespaceName: "tlr123",
            NamespaceHostAddressCidr: "169.254.123.1/30",
            NamespacePeerAddressCidr: "169.254.123.2/30",
            HostInterfaceName: "tlrf123",
            NamespaceInterfaceName: "tlrf123n",
            Routes: [new TeamLabStaticRouteRequest("not-a-cidr", "10.250.0.12")],
            DryRun: true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Message);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task CreateBridgeAsync_DisabledExecutionReportsCapabilityDisabled()
    {
        var service = CreateBridgeService(enable: false);

        var result = await service.ApplyAsync(new TeamLabBridgeRequest(
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
        using var stateRoot = new TempDirectory();
        var stateDirectory = Path.Combine(stateRoot.Path, "runtime-123");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(Path.Combine(stateDirectory, "active-generation.json"),
            JsonSerializer.Serialize(new
            {
                runtimeId = 123,
                generation = 1,
                activatedAt = DateTimeOffset.UtcNow
            }));
        var runner = new PrivateKeyAwareTeamLabCommandRunner(ValidInterfacePrivateKey);
        var service = CreateService(enable: true, runner, dryRun: false, runtimeStateRoot: stateRoot.Path);

        var result = await service.ConfigureWireGuardAsync(new TeamLabWireGuardRequest(
            RuntimeId: 123,
            Generation: 1,
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

    [Fact]
    public void TeamLabCommandRunner_NormalizesShellCommandLineEndings()
    {
        var startInfo = TeamLabCommandRunner.CreateStartInfo("first\r\nsecond\r\n", redirectStandardInput: false);

        Assert.Equal(["-c", "first\nsecond\n"], startInfo.ArgumentList);
    }

    [Fact]
    public void TeamLabCommandRunner_ReportsExitCodeWhenCommandHasNoOutput()
    {
        Assert.Equal(
            "Command failed with exit code 17.",
            TeamLabCommandRunner.NormalizeFailureOutput(17, string.Empty));
        Assert.Equal("permission denied", TeamLabCommandRunner.NormalizeFailureOutput(1, "permission denied"));
    }

    private static TeamLabNetworkService CreateService(
        bool enable,
        TeamLabCommandRunner? runner = null,
        bool dryRun = true,
        string? runtimeStateRoot = null)
    {
        var config = new AgentTeamLabConfig { Enable = enable, DryRun = dryRun };
        if (runtimeStateRoot is not null) config.RuntimeStateRoot = runtimeStateRoot;
        var options = Options.Create(config);
        runner ??= new TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance);
        var commandExecutor = new TeamLabCommandExecutor(
            options,
            runner,
            NullLogger<TeamLabCommandExecutor>.Instance);
        var bridge = new TeamLabBridgeService(commandExecutor);
        var router = new TeamLabRouterService(commandExecutor);
        var firewall = new TeamLabFirewallService(commandExecutor, options);
        var fabric = new TeamLabFabricService(
            commandExecutor,
            firewall,
            runner,
            new TeamLabFabricRouteStore(options),
            options,
            NullLogger<TeamLabFabricService>.Instance);
        var registry = new ObservationPointRegistry(NullLogger<ObservationPointRegistry>.Instance);
        var spool = new ObservationBatchSpool(options, NullLogger<ObservationBatchSpool>.Instance);
        var sensors = new EndpointSensorChannelService(
            spool, NullLogger<EndpointSensorChannelService>.Instance);
        var uploader = new PcapSegmentUploader(
            new Mock<IHttpClientFactory>().Object,
            Options.Create(new AgentConfig { NodeId = Guid.NewGuid() }));
        var pcap = new TeamLabPcapService(
            registry,
            uploader,
            commandExecutor,
            options,
            NullLogger<TeamLabPcapService>.Instance);
        var guest = new VmGuestAgentService(NullLogger<VmGuestAgentService>.Instance);
        var bootstrap = new VmBootstrapService(
            guest,
            NullLogger<VmBootstrapService>.Instance);
        return new TeamLabNetworkService(
            options,
            runner,
            bridge,
            router,
            fabric,
            firewall,
            registry,
            spool,
            sensors,
            pcap,
            bootstrap,
            new TeamLabRuntimeGenerationStore(options),
            new TeamLabOvsAttachmentProvider(new OvsdbJsonRpcClient(), options),
            new AgentResourceLock(),
            NullLogger<TeamLabNetworkService>.Instance);
    }

    private static TeamLabBridgeService CreateBridgeService(bool enable)
    {
        var options = Options.Create(new AgentTeamLabConfig { Enable = enable, DryRun = true });
        var executor = new TeamLabCommandExecutor(
            options,
            new TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance),
            NullLogger<TeamLabCommandExecutor>.Instance);
        return new TeamLabBridgeService(executor);
    }

    private static TeamLabRouterService CreateRouterService(bool enable)
    {
        var options = Options.Create(new AgentTeamLabConfig { Enable = enable, DryRun = true });
        var executor = new TeamLabCommandExecutor(
            options,
            new TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance),
            NullLogger<TeamLabCommandExecutor>.Instance);
        return new TeamLabRouterService(executor);
    }

    private static TeamLabFabricService CreateFabricService(bool enable)
    {
        var options = Options.Create(new AgentTeamLabConfig { Enable = enable, DryRun = true });
        var runner = new TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance);
        var executor = new TeamLabCommandExecutor(
            options,
            runner,
            NullLogger<TeamLabCommandExecutor>.Instance);
        return new TeamLabFabricService(
            executor,
            new TeamLabFirewallService(executor, options),
            runner,
            new TeamLabFabricRouteStore(options),
            options,
            NullLogger<TeamLabFabricService>.Instance);
    }

    private static TeamLabInfrastructureApplyRequest InfrastructureRequest(int runtimeId, bool dryRun) => new(
        runtimeId,
        Generation: 3,
        RouteVersion: 3,
        RouterNamespace: "tlr123",
        Switches:
        [
            new TeamLabManagedSwitchIntent(
                "entry",
                "Entry",
                "10.10.0.0/24",
                "10.10.0.1",
                "tl123-entry",
                "tld123-entry",
                [new TeamLabDhcpLeaseRequest("02:42:0a:0a:00:0a", "10.10.0.10", "entry")])
        ],
        Routers: [new TeamLabManagedRouterFragmentIntent("router", ["entry"])],
        Fabric: new TeamLabFabricUplinkIntent(
            "10.24.0.31",
            "169.254.123.1/30",
            "169.254.123.2/30",
            "tlrf123",
            "tlrf123n",
            [new TeamLabStaticRouteRequest("10.10.0.0/24", "169.254.123.2")],
            []),
        ForwardPolicies: [],
        ObservationPoints:
        [
            new TeamLabObservationPointIntent(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "entry",
                0,
                "tl123-entry")
        ],
        DryRun: dryRun);

    private static KvmService CreateKvmService(string imageStoragePath) => new(
        Options.Create(new KvmConfig { ImageStoragePath = imageStoragePath }),
        new AgentResourceLock(),
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

    private sealed class InfrastructureStateTeamLabCommandRunner(bool probeHealthy) : TeamLabCommandRunner(
        NullLogger<TeamLabCommandRunner>.Instance)
    {
        public List<string> Commands { get; } = [];

        public override Task<(bool Success, string Output)> RunAsync(
            string command,
            string? standardInput,
            CancellationToken token)
        {
            Commands.Add(command);
            if (command.StartsWith("wg show ", StringComparison.Ordinal))
                return Task.FromResult((true, "peer-public-key\t10.250.0.2/32\n"));
            if (command.Contains("ip netns exec tlr123 ip link show tlr123n0", StringComparison.Ordinal))
                return Task.FromResult(probeHealthy
                    ? (true, string.Empty)
                    : (false, "router namespace interface is missing"));
            return Task.FromResult((true, string.Empty));
        }
    }

    private sealed class DnsmasqFailureTeamLabCommandRunner : TeamLabCommandRunner
    {
        public DnsmasqFailureTeamLabCommandRunner() : base(NullLogger<TeamLabCommandRunner>.Instance)
        {
        }

        public override Task<(bool Success, string Output)> RunAsync(
            string command,
            string? standardInput,
            CancellationToken token) =>
            Task.FromResult(
                command.Contains("dnsmasq failed readiness checks", StringComparison.Ordinal)
                    ? (false, "dnsmasq did not become ready")
                    : command.Contains("dnsmasq_sockets=", StringComparison.Ordinal)
                        ? (false, "dnsmasq live fact is missing")
                        : (true, string.Empty));
    }

    private sealed class ProbeFailureTeamLabCommandRunner : TeamLabCommandRunner
    {
        public ProbeFailureTeamLabCommandRunner() : base(NullLogger<TeamLabCommandRunner>.Instance)
        {
        }

        public override Task<(bool Success, string Output)> RunAsync(
            string command,
            string? standardInput,
            CancellationToken token) =>
            Task.FromResult(command.Contains("/dev/tcp/", StringComparison.Ordinal)
                ? (false, "connection refused")
                : (true, string.Empty));
    }

    private sealed class FabricPeerTeamLabCommandRunner : TeamLabCommandRunner
    {
        private string[] _allowedIps = ["10.250.0.2/32"];

        public FabricPeerTeamLabCommandRunner() : base(NullLogger<TeamLabCommandRunner>.Instance)
        {
        }

        public List<string> Commands { get; } = [];
        public IReadOnlyCollection<string> AllowedIps => _allowedIps;

        public void ReplaceAllowedIps(string[] allowedIps) => _allowedIps = allowedIps;

        public override Task<(bool Success, string Output)> RunAsync(string command, CancellationToken token)
        {
            Commands.Add(command);
            if (command.StartsWith("wg show ", StringComparison.Ordinal))
                return Task.FromResult((true,
                    $"peer-public-key\t{(_allowedIps.Length == 0 ? "(none)" : string.Join(' ', _allowedIps))}\n"));
            const string marker = "allowed-ips '";
            var start = command.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                start += marker.Length;
                var end = command.IndexOf('\'', start);
                _allowedIps = command[start..end]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            return Task.FromResult((true, string.Empty));
        }
    }
}
