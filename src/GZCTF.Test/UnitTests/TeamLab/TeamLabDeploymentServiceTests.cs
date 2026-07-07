using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Internal;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Services;
using GZCTF.Services.TeamLab;
using GZCTF.Services.Fleet;
using GZCTF.Models.Data;
using GZCTF.Utils;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabDeploymentServiceTests
{
    [Fact]
    public void DeploymentPlan_UsesTraceableLinuxResourceNames()
    {
        var names = TeamLabDeploymentService.BuildResourceNames(runtimeId: 123, networkKeys: ["dmz", "data"]);

        Assert.All(names.Bridges, name => Assert.True(name.Length <= 15));
        Assert.Contains(names.Bridges, name => name.StartsWith("tl123-"));
        Assert.True(names.RouterNamespace.Length <= 15);
        Assert.True(names.WireGuardInterface.Length <= 15);
    }

    [Theory]
    [InlineData(PenetrationRuntimeStatus.Running, true)]
    [InlineData(PenetrationRuntimeStatus.Pending, false)]
    [InlineData(PenetrationRuntimeStatus.CreatingContainers, false)]
    [InlineData(PenetrationRuntimeStatus.Failed, false)]
    [InlineData(PenetrationRuntimeStatus.CleanupPending, false)]
    [InlineData(PenetrationRuntimeStatus.Stopped, false)]
    public void CanOpenToPlayers_RequiresRunningPenetrationEnvironment(PenetrationRuntimeStatus status, bool expected)
    {
        Assert.Equal(expected, TeamLabDeploymentService.CanOpenToPlayers(status));
    }

    [Theory]
    [InlineData(OSType.Linux, 24)]
    [InlineData(OSType.Windows, 72)]
    public void ResolveNativeVmReadyProbeAttempts_GivesWindowsEnoughColdBootTime(OSType osType, int expected)
    {
        Assert.Equal(expected, TeamLabDeploymentService.ResolveNativeVmReadyProbeAttempts(osType));
    }

    [Fact]
    public void RecordRuntimeFacts_AddsTraceableNetworksAndAssets()
    {
        var runtime = new TeamLabRuntime
        {
            Id = 123,
            PublicUdpMapping = new TeamLabPublicUdpMapping { PublicUdpPort = 32001 }
        };
        var names = TeamLabDeploymentService.BuildResourceNames(123, ["entry", "lab"]);

        TeamLabDeploymentService.RecordRuntimeFacts(
            runtime,
            names,
            entryCidr: "10.180.1.0/24",
            entryGateway: "10.180.1.1",
            labCidr: "10.180.2.0/24",
            labGateway: "10.180.2.1");

        Assert.Collection(runtime.Networks.OrderBy(n => n.TopologyKey),
            network =>
            {
                Assert.Equal("entry", network.TopologyKey);
                Assert.Equal("10.180.1.0/24", network.Cidr);
                Assert.Equal("10.180.1.1", network.GatewayIp);
                Assert.Equal(names.Bridges[0], network.BridgeName);
            },
            network =>
            {
                Assert.Equal("lab", network.TopologyKey);
                Assert.Equal("10.180.2.0/24", network.Cidr);
                Assert.Equal("10.180.2.1", network.GatewayIp);
                Assert.Equal(names.Bridges[1], network.BridgeName);
            });

        Assert.Contains(runtime.Assets,
            asset => asset.Kind == TeamLabResourceKind.RouterNamespace &&
                     asset.RuntimeResourceId == names.RouterNamespace &&
                     asset.Status == TeamLabRuntimeStatus.Running);
        Assert.Contains(runtime.Assets,
            asset => asset.Kind == TeamLabResourceKind.WireGuard &&
                     asset.RuntimeResourceId == names.WireGuardInterface &&
                     asset.Status == TeamLabRuntimeStatus.Running);
        Assert.Contains(runtime.Assets,
            asset => asset.Kind == TeamLabResourceKind.PublicUdpMapping &&
                     asset.RuntimeResourceId == "32001" &&
                     asset.Status == TeamLabRuntimeStatus.Running);
    }

    [Fact]
    public void RecordRuntimeAsset_TracksDockerSourceAndInterfaceFacts()
    {
        var runtime = new TeamLabRuntime { Id = 123 };

        TeamLabDeploymentService.RecordRuntimeAsset(runtime, new TeamLabRuntimeAssetSpec(
            TeamLabResourceKind.Docker,
            TopologyKey: "portal",
            Name: "Portal Web",
            RuntimeResourceId: "container-1",
            SourceTemplateId: 42,
            Image: "registry.local/portal:latest",
            NetworkKey: "dmz",
            IpAddress: "10.180.1.10",
            MacAddress: "02:42:ac:10:00:02",
            InterfaceSummaryJson: """[{"networkKey":"dmz","ipAddress":"10.180.1.10","macAddress":"02:42:ac:10:00:02"}]"""));

        var asset = Assert.Single(runtime.Assets);
        Assert.Equal(TeamLabResourceKind.Docker, asset.Kind);
        Assert.Equal("portal", asset.TopologyKey);
        Assert.Equal("Portal Web", asset.Name);
        Assert.Equal("container-1", asset.RuntimeResourceId);
        Assert.Equal(42, asset.SourceTemplateId);
        Assert.Equal("registry.local/portal:latest", asset.Image);
        Assert.Equal("dmz", asset.NetworkKey);
        Assert.Equal("10.180.1.10", asset.IpAddress);
        Assert.Equal("02:42:ac:10:00:02", asset.MacAddress);
        Assert.Contains("\"networkKey\":\"dmz\"", asset.InterfaceSummaryJson);
        Assert.Equal(TeamLabRuntimeStatus.Running, asset.Status);
    }

    [Fact]
    public void MarkRuntimeFactsDestroyed_ClosesAllTrackedAssets()
    {
        var runtime = new TeamLabRuntime
        {
            Assets =
            [
                new TeamLabRuntimeAsset { Kind = TeamLabResourceKind.RouterNamespace, Status = TeamLabRuntimeStatus.Running },
                new TeamLabRuntimeAsset { Kind = TeamLabResourceKind.WireGuard, Status = TeamLabRuntimeStatus.Running }
            ]
        };

        TeamLabDeploymentService.MarkRuntimeFactsDestroyed(runtime);

        Assert.All(runtime.Assets, asset => Assert.Equal(TeamLabRuntimeStatus.Destroyed, asset.Status));
    }

    [Fact]
    public void BuildRuntimeEvent_SplitsLongMessageIntoBoundedMessageAndDetail()
    {
        var message = new string('x', 1500);

        var evt = TeamLabDeploymentService.BuildRuntimeEvent("deploy", TeamLabEventLevel.Error, message);

        Assert.Equal("deploy", evt.Stage);
        Assert.Equal(TeamLabEventLevel.Error, evt.Level);
        Assert.True(evt.Message.Length <= 256);
        Assert.True(evt.Detail?.Length <= 1024);
        Assert.StartsWith(new string('x', 250), evt.Message);
        Assert.StartsWith(new string('x', 1000), evt.Detail);
    }

    [Fact]
    public void NormalizeRuntimeError_ClampsLongDatabaseErrorFields()
    {
        var message = new string('x', 1500);

        var normalized = TeamLabDeploymentService.NormalizeRuntimeError(message);

        Assert.True(normalized.Length <= 1024);
        Assert.StartsWith(new string('x', 1000), normalized);
    }

    [Fact]
    public void BuildNativeDockerContainerConfig_UsesNoPublicPortAndFixedWorkerNode()
    {
        var workerNodeId = System.Guid.NewGuid();
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Docker,
            TopologyKey: "portal",
            Name: "Portal",
            SourceTemplateId: 42,
            Image: "registry.local/portal:latest",
            CpuCount: 10,
            MemoryLimit: 512,
            StorageLimit: 256,
            ExposePort: 8080,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "public", "tl12-public", "eth0", "10.90.0.2", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
            ]);

        var config = TeamLabDeploymentService.BuildNativeDockerContainerConfig(spec, teamId: 7,
            workerNodeId, flag: "flag{portal}");

        Assert.Equal("registry.local/portal:latest", config.Image);
        Assert.Equal("7", config.TeamId);
        Assert.Equal(8080, config.ExposedPort);
        Assert.False(config.PublishPort);
        Assert.True(config.BypassPublicProxy);
        Assert.False(config.UsePenetrationFabric);
        Assert.True(config.UseHostNetworkNone);
        Assert.Equal(workerNodeId, config.PreferredNodeId);
        Assert.Equal("flag{portal}", config.Flag);
        Assert.Empty(config.NetworkAttachments);
        Assert.Equal(NetworkMode.Custom, config.NetworkMode);
    }

    [Fact]
    public void BuildNativeDockerContainerConfig_InjectsOnlyAttachedNetworkDnsAtContainerCreation()
    {
        var workerNodeId = System.Guid.NewGuid();
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Docker,
            TopologyKey: "portal",
            Name: "Portal",
            SourceTemplateId: 42,
            Image: "registry.local/portal:latest",
            CpuCount: 10,
            MemoryLimit: 512,
            StorageLimit: 256,
            ExposePort: 8080,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "entry", "tl12-entry", "eth0", "10.90.0.3", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
            ]);

        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("entry", "Entry", "10.90.0.0/28", "10.90.0.1", "tl12-entry"),
            new TeamLabRuntimeNetworkSpec("data", "Data", "10.90.0.16/28", "10.90.0.17", "tl12-data")
        };
        var config = TeamLabDeploymentService.BuildNativeDockerContainerConfig(spec, teamId: 7,
            workerNodeId, flag: null, networks);

        Assert.Equal(["10.90.0.1"], config.DnsServers);
    }

    [Fact]
    public async Task BuildResolvedNativeDockerContainerConfig_RewritesInternalRegistryReferenceForAgent()
    {
        var workerNodeId = System.Guid.NewGuid();
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Docker,
            TopologyKey: "pwn",
            Name: "Pwn",
            SourceTemplateId: 114,
            Image: "gzctf-internal://ctf/pwn/21:latest",
            CpuCount: 10,
            MemoryLimit: 512,
            StorageLimit: 256,
            ExposePort: 80,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "service", "tl12-service", "eth0", "10.90.0.3", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
            ]);
        var registry = CreateDockerRegistryService("10.24.0.28:5000");

        var config = await TeamLabDeploymentService.BuildResolvedNativeDockerContainerConfigAsync(
            spec, teamId: 7, workerNodeId, flag: null, registry, CancellationToken.None);

        Assert.Equal("10.24.0.28:5000/ctf/pwn/21:latest", config.Image);
        Assert.False(config.PublishPort);
        Assert.True(config.UseHostNetworkNone);
        Assert.Equal(workerNodeId, config.PreferredNodeId);
    }

    [Fact]
    public void ResolveDeploymentMode_AlwaysUsesNativePublishedTopologyForTeamLab()
    {
        var mode = TeamLabDeploymentService.ResolveDeploymentMode(new PenetrationTeamEnvironment
        {
            Status = PenetrationRuntimeStatus.Running,
            NodeId = System.Guid.NewGuid()
        });

        Assert.Equal(TeamLabDeploymentMode.NativePublishedTopology, mode);
    }

    [Fact]
    public void BuildRuntimeAssetRecord_PreservesAllTeamLabInterfaces()
    {
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Docker,
            TopologyKey: "portal",
            Name: "Portal",
            SourceTemplateId: 42,
            Image: "registry.local/portal:latest",
            CpuCount: 10,
            MemoryLimit: 512,
            StorageLimit: 256,
            ExposePort: 8080,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "public", "tl12-public", "eth0", "10.90.0.2", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false),
                new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth1", "10.90.0.18", 28,
                    "02:42:ac:10:00:03", IsPrimary: false, RemoveDefaultRoute: false)
            ]);

        var record = TeamLabDeploymentService.BuildRuntimeAssetRecord(spec, runtimeResourceId: "container-1");

        Assert.Equal(TeamLabResourceKind.Docker, record.Kind);
        Assert.Equal("public", record.NetworkKey);
        Assert.Equal("10.90.0.2", record.IpAddress);
        Assert.Equal("02:42:ac:10:00:02", record.MacAddress);
        Assert.Contains("\"networkKey\":\"public\"", record.InterfaceSummaryJson);
        Assert.Contains("\"networkKey\":\"data\"", record.InterfaceSummaryJson);
    }

    [Fact]
    public void BuildNativeVmRequest_UsesTeamLabBridgeInterfaces()
    {
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Vm,
            TopologyKey: "win-ad",
            Name: "Windows AD",
            SourceTemplateId: 7,
            Image: "/images/win.qcow2",
            CpuCount: 20,
            MemoryLimit: 4096,
            StorageLimit: 40960,
            ExposePort: 3389,
            InfrastructureRole: "DomainController",
            StartPriority: 10,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth0", "10.90.0.19", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false),
                new TeamLabAssetInterfaceSpec("asset", "ops", "tl12-ops", "eth1", "10.90.0.35", 28,
                    "02:42:ac:10:00:03", IsPrimary: false, RemoveDefaultRoute: false)
            ],
            OSType: OSType.Windows);

        var request = TeamLabDeploymentService.BuildNativeVmRequest(runtimeId: 12, spec, flag: "flag{win}");

        Assert.Equal(7, request.TemplateId);
        Assert.Equal("/images/win.qcow2", request.TemplatePath);
        Assert.Equal("tl12-win-ad", request.VmName);
        Assert.Equal(4096, request.Memory);
        Assert.Equal(20, request.Cpu);
        Assert.Equal("flag{win}", request.Flag);
        Assert.Collection(request.Interfaces,
            iface =>
            {
                Assert.Equal("tl12-data", iface.BridgeName);
                Assert.Equal("02:42:ac:10:00:02", iface.MacAddress);
                Assert.Equal("e1000e", iface.Model);
            },
            iface =>
            {
                Assert.Equal("tl12-ops", iface.BridgeName);
                Assert.Equal("02:42:ac:10:00:03", iface.MacAddress);
                Assert.Equal("e1000e", iface.Model);
            });
    }

    [Fact]
    public void BuildNativeVmRequest_KeepsVirtioForLinuxTemplates()
    {
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Vm,
            TopologyKey: "linux-core",
            Name: "Linux Core",
            SourceTemplateId: 8,
            Image: "/images/linux.qcow2",
            CpuCount: 2,
            MemoryLimit: 1024,
            StorageLimit: 20480,
            ExposePort: 22,
            InfrastructureRole: null,
            StartPriority: 20,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth0", "10.90.0.20", 28,
                    "02:42:ac:10:00:04", IsPrimary: true, RemoveDefaultRoute: false)
            ],
            OSType: OSType.Linux);

        var request = TeamLabDeploymentService.BuildNativeVmRequest(runtimeId: 12, spec, flag: "flag{linux}");

        var iface = Assert.Single(request.Interfaces);
        Assert.Equal("virtio", iface.Model);
    }

    [Theory]
    [InlineData("Ready", "10.90.0.19", true)]
    [InlineData("Running", "10.90.0.19", true)]
    [InlineData("Pending", "10.90.0.19", false)]
    [InlineData("Ready", "10.90.0.20", false)]
    [InlineData("Ready", "", false)]
    public void ValidateNativeVmReady_RequiresActualPrimaryIp(string status, string? actualIp, bool expected)
    {
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Vm,
            TopologyKey: "win-ad",
            Name: "Windows AD",
            SourceTemplateId: 7,
            Image: "/images/win.qcow2",
            CpuCount: 20,
            MemoryLimit: 4096,
            StorageLimit: 40960,
            ExposePort: 3389,
            InfrastructureRole: "DomainController",
            StartPriority: 10,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth0", "10.90.0.19", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
            ]);

        var result = TeamLabDeploymentService.ValidateNativeVmReady(spec,
            new AgentVmIpResponse { VmName = "tl12-win-ad", IpAddress = actualIp, Status = status });

        Assert.Equal(expected, result.Success);
        if (!expected)
            Assert.Contains("Windows AD", result.Message);
    }

    [Fact]
    public void BuildNativeProbeTargets_SkipsWindowsVmInterfacesAfterDhcpReadiness()
    {
        var assets = new[]
        {
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Vm,
                TopologyKey: "router",
                Name: "Router",
                SourceTemplateId: 7,
                Image: "/images/router.qcow2",
                CpuCount: 20,
                MemoryLimit: 4096,
                StorageLimit: 40960,
                ExposePort: 22,
                InfrastructureRole: "Router",
                StartPriority: 10,
                Interfaces:
                [
                    new TeamLabAssetInterfaceSpec("router", "entry", "tl12-entry", "eth0", "10.90.0.3", 28,
                        "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false),
                    new TeamLabAssetInterfaceSpec("router", "core", "tl12-core", "eth1", "10.90.0.19", 28,
                        "02:42:ac:10:00:03", IsPrimary: false, RemoveDefaultRoute: false),
                    new TeamLabAssetInterfaceSpec("router", "data", "tl12-data", "eth2", "10.90.0.35", 28,
                        "02:42:ac:10:00:04", IsPrimary: false, RemoveDefaultRoute: false)
                ],
                OSType: OSType.Windows),
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Vm,
                TopologyKey: "linux-jump",
                Name: "Linux Jump",
                SourceTemplateId: 9,
                Image: "/images/linux.qcow2",
                CpuCount: 2,
                MemoryLimit: 2048,
                StorageLimit: 20480,
                ExposePort: 22,
                InfrastructureRole: null,
                StartPriority: 20,
                Interfaces:
                [
                    new TeamLabAssetInterfaceSpec("asset", "entry", "tl12-entry", "eth0", "10.90.0.5", 28,
                        "02:42:ac:10:00:07", IsPrimary: true, RemoveDefaultRoute: false)
                ],
                OSType: OSType.Linux),
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Docker,
                TopologyKey: "portal",
                Name: "Portal",
                SourceTemplateId: 8,
                Image: "portal:latest",
                CpuCount: 10,
                MemoryLimit: 512,
                StorageLimit: 256,
                ExposePort: 8080,
                InfrastructureRole: null,
                StartPriority: 50,
                Interfaces:
                [
                    new TeamLabAssetInterfaceSpec("portal", "entry", "tl12-entry", "eth0", "10.90.0.4", 28,
                        "02:42:ac:10:00:05", IsPrimary: true, RemoveDefaultRoute: false),
                    new TeamLabAssetInterfaceSpec("portal", "entry", "tl12-entry", "eth1", "10.90.0.4", 28,
                        "02:42:ac:10:00:06", IsPrimary: false, RemoveDefaultRoute: false)
                ])
        };

        var targets = TeamLabDeploymentService.BuildNativeProbeTargets(assets);

        Assert.Equal(["10.90.0.5", "10.90.0.4"], targets);
    }

    [Fact]
    public void ShouldRunNativeConnectivityProbe_SkipsAllWindowsVmTopology()
    {
        var assets = new[]
        {
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Vm,
                TopologyKey: "win-entry",
                Name: "Windows Entry",
                SourceTemplateId: 7,
                Image: "/images/windows.qcow2",
                CpuCount: 20,
                MemoryLimit: 4096,
                StorageLimit: 40960,
                ExposePort: 3389,
                InfrastructureRole: null,
                StartPriority: 10,
                Interfaces:
                [
                    new TeamLabAssetInterfaceSpec("asset", "entry", "tl12-entry", "eth0", "10.90.0.3", 28,
                        "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
                ],
                OSType: OSType.Windows),
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Vm,
                TopologyKey: "win-data",
                Name: "Windows Data",
                SourceTemplateId: 7,
                Image: "/images/windows.qcow2",
                CpuCount: 20,
                MemoryLimit: 4096,
                StorageLimit: 40960,
                ExposePort: 3389,
                InfrastructureRole: null,
                StartPriority: 20,
                Interfaces:
                [
                    new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth0", "10.90.0.19", 28,
                        "02:42:ac:10:00:03", IsPrimary: true, RemoveDefaultRoute: false)
                ],
                OSType: OSType.Windows)
        };

        Assert.False(TeamLabDeploymentService.ShouldRunNativeConnectivityProbe(assets));
    }

    [Fact]
    public void ShouldRunNativeConnectivityProbe_RequiresProbeForDockerOrLinuxAssets()
    {
        var assets = new[]
        {
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Docker,
                TopologyKey: "portal",
                Name: "Portal",
                SourceTemplateId: 8,
                Image: "portal:latest",
                CpuCount: 10,
                MemoryLimit: 512,
                StorageLimit: 256,
                ExposePort: 8080,
                InfrastructureRole: null,
                StartPriority: 50,
                Interfaces:
                [
                    new TeamLabAssetInterfaceSpec("portal", "entry", "tl12-entry", "eth0", "10.90.0.4", 28,
                        "02:42:ac:10:00:05", IsPrimary: true, RemoveDefaultRoute: false)
                ])
        };

        Assert.True(TeamLabDeploymentService.ShouldRunNativeConnectivityProbe(assets));
    }

    [Fact]
    public void CountAssetSlots_CountsOnlyDockerAndVmRuntimeAssets()
    {
        var assets = new[]
        {
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Docker,
                TopologyKey: "portal",
                Name: "Portal",
                SourceTemplateId: 8,
                Image: "portal:latest",
                CpuCount: 10,
                MemoryLimit: 512,
                StorageLimit: 256,
                ExposePort: 8080,
                InfrastructureRole: null,
                StartPriority: 50,
                Interfaces: []),
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Vm,
                TopologyKey: "linux-jump",
                Name: "Linux Jump",
                SourceTemplateId: 9,
                Image: "/images/linux.qcow2",
                CpuCount: 2,
                MemoryLimit: 2048,
                StorageLimit: 20480,
                ExposePort: 22,
                InfrastructureRole: null,
                StartPriority: 20,
                Interfaces: []),
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Docker,
                TopologyKey: "db",
                Name: "Database",
                SourceTemplateId: 10,
                Image: "db:latest",
                CpuCount: 10,
                MemoryLimit: 512,
                StorageLimit: 256,
                ExposePort: 5432,
                InfrastructureRole: null,
                StartPriority: 60,
                Interfaces: [])
        };

        var slots = TeamLabDeploymentService.CountAssetSlots(assets);

        Assert.Equal(2, slots.DockerSlots);
        Assert.Equal(1, slots.VmSlots);
    }

    [Fact]
    public async Task TryReserveTeamLabCapacityAsync_ReservesWholeTopologyOnRuntimeNodeOnly()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = System.Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Name = "teamlab-node",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            MaxContainers = 3,
            MaxVms = 1,
            CurrentContainers = 1,
            CurrentVms = 0,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.2"
        };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var service = CreateDeploymentService(context);
        var runtime = new TeamLabRuntime { Id = 7, WorkerNodeId = node.Id };

        var result = await service.TryReserveTeamLabCapacityAsync(runtime,
            new TeamLabAssetSlotCount(DockerSlots: 2, VmSlots: 1), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var reloaded = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(3, reloaded.CurrentContainers);
        Assert.Equal(1, reloaded.CurrentVms);
    }

    [Fact]
    public async Task TryReserveTeamLabCapacityAsync_RejectsWholeTopologyWhenRuntimeNodeCapacityIsInsufficient()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = System.Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Name = "teamlab-node",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            MaxContainers = 2,
            MaxVms = 1,
            CurrentContainers = 1,
            CurrentVms = 0,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.2"
        };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var service = CreateDeploymentService(context);
        var runtime = new TeamLabRuntime { Id = 8, WorkerNodeId = node.Id };

        var result = await service.TryReserveTeamLabCapacityAsync(runtime,
            new TeamLabAssetSlotCount(DockerSlots: 2, VmSlots: 1), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("capacity", result.Message, StringComparison.OrdinalIgnoreCase);
        var reloaded = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(1, reloaded.CurrentContainers);
        Assert.Equal(0, reloaded.CurrentVms);
    }

    [Fact]
    public async Task ReleaseTeamLabCapacityAsync_ReleasesTopologySlotsExactlyOnce()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = System.Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Name = "teamlab-node",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            MaxContainers = 4,
            MaxVms = 2,
            CurrentContainers = 2,
            CurrentVms = 1,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.2"
        };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var service = CreateDeploymentService(context);
        var runtime = new TeamLabRuntime
        {
            Id = 9,
            WorkerNodeId = node.Id,
            Assets =
            [
                new TeamLabRuntimeAsset { Kind = TeamLabResourceKind.Docker, Status = TeamLabRuntimeStatus.Running },
                new TeamLabRuntimeAsset { Kind = TeamLabResourceKind.Docker, Status = TeamLabRuntimeStatus.Failed },
                new TeamLabRuntimeAsset { Kind = TeamLabResourceKind.Vm, Status = TeamLabRuntimeStatus.Running },
                new TeamLabRuntimeAsset { Kind = TeamLabResourceKind.RouterNamespace, Status = TeamLabRuntimeStatus.Running }
            ]
        };

        await service.ReleaseTeamLabCapacityAsync(runtime, CancellationToken.None);
        await service.ReleaseTeamLabCapacityAsync(runtime, CancellationToken.None);

        var reloaded = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(0, reloaded.CurrentContainers);
        Assert.Equal(0, reloaded.CurrentVms);
    }

    [Fact]
    public async Task ReleaseTeamLabCapacityAsync_ReleasesPlannedSlotsWhenRuntimeAssetsAreNotRecordedYet()
    {
        await using var context = CreateContext();
        var node = new WorkerNode
        {
            Id = System.Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Name = "teamlab-node",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            MaxContainers = 4,
            MaxVms = 2,
            CurrentContainers = 2,
            CurrentVms = 1,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.2"
        };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var service = CreateDeploymentService(context);
        var runtime = new TeamLabRuntime
        {
            Id = 10,
            WorkerNodeId = node.Id
        };

        await service.ReleaseTeamLabCapacityAsync(runtime,
            new TeamLabAssetSlotCount(DockerSlots: 2, VmSlots: 1),
            CancellationToken.None);

        var reloaded = await context.WorkerNodes.SingleAsync(n => n.Id == node.Id);
        Assert.Equal(0, reloaded.CurrentContainers);
        Assert.Equal(0, reloaded.CurrentVms);
    }

    [Fact]
    public async Task TryQueueTeamLabRuntimeAsync_CreatesDurableQueueTicketWithoutSecrets()
    {
        await using var context = CreateContext();
        var service = CreateDeploymentService(context);
        var runtime = new TeamLabRuntime { Id = 11, GameId = 5, TeamId = 7 };

        var queue = await service.TryQueueTeamLabRuntimeAsync(runtime,
            new TeamLabAssetSlotCount(DockerSlots: 3, VmSlots: 1),
            "capacity exhausted",
            CancellationToken.None);

        Assert.NotNull(queue);
        Assert.Equal(DeploymentQueueKind.TeamLabRuntime, queue!.Kind);
        Assert.Equal(1, queue.QueuePosition);
        Assert.DoesNotContain("PrivateKey", queue.ToString(), StringComparison.OrdinalIgnoreCase);

        var ticket = Assert.Single(context.DeploymentQueueTickets);
        Assert.Equal("teamlab-runtime:5:7:11", ticket.ActiveIdentity);
        Assert.Equal(3, ticket.DockerSlots);
        Assert.Equal(1, ticket.VmSlots);
        Assert.Equal(DeploymentQueueTicketStatus.Pending, ticket.Status);
    }

    [Fact]
    public void BuildNativeContainerAttachRequests_UsesOnlyAllowedNetworkRoutes()
    {
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Docker,
            TopologyKey: "portal",
            Name: "Portal",
            SourceTemplateId: 42,
            Image: "registry.local/portal:latest",
            CpuCount: 10,
            MemoryLimit: 512,
            StorageLimit: 256,
            ExposePort: 8080,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "public", "tl12-public", "eth0", "10.90.0.3", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
            ]);
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("public", "Public", "10.90.0.0/28", "10.90.0.1", "tl12-public"),
            new TeamLabRuntimeNetworkSpec("data", "Data", "10.90.0.16/28", "10.90.0.17", "tl12-data")
        };

        var requests = TeamLabDeploymentService.BuildNativeContainerAttachRequests(
            runtimeId: 12,
            containerId: "container-1",
            spec,
            networks,
            vpnClientCidr: "10.90.0.2/32",
            allowedNetworkCidrsByNetworkKey: new Dictionary<string, string[]>
            {
                ["public"] = ["10.90.0.16/28"]
            },
            dnsServers: [],
            dryRun: false);

        var request = Assert.Single(requests);
        Assert.Equal("10.90.0.1", request.GatewayIp);
        Assert.Contains("10.90.0.2/32", request.StaticRoutes);
        Assert.Contains("10.90.0.16/28", request.StaticRoutes);
        Assert.DoesNotContain("10.90.0.0/28", request.StaticRoutes);
    }

    [Fact]
    public void BuildNativeContainerAttachRequests_DoesNotRouteToUnlinkedNetworks()
    {
        var spec = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Docker,
            TopologyKey: "portal",
            Name: "Portal",
            SourceTemplateId: 42,
            Image: "registry.local/portal:latest",
            CpuCount: 10,
            MemoryLimit: 512,
            StorageLimit: 256,
            ExposePort: 8080,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "public", "tl12-public", "eth0", "10.90.0.3", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
            ]);
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("public", "Public", "10.90.0.0/28", "10.90.0.1", "tl12-public"),
            new TeamLabRuntimeNetworkSpec("data", "Data", "10.90.0.16/28", "10.90.0.17", "tl12-data")
        };

        var request = Assert.Single(TeamLabDeploymentService.BuildNativeContainerAttachRequests(
            runtimeId: 12,
            containerId: "container-1",
            spec,
            networks,
            vpnClientCidr: "10.90.0.2/32",
            allowedNetworkCidrsByNetworkKey: new Dictionary<string, string[]>(),
            dnsServers: [],
            dryRun: false));

        Assert.Contains("10.90.0.2/32", request.StaticRoutes);
        Assert.DoesNotContain("10.90.0.16/28", request.StaticRoutes);
    }

    [Fact]
    public void BuildNativeDockerContainerConfig_EnablesForwardingOnlyForRoutingAssets()
    {
        var workerNodeId = System.Guid.NewGuid();
        var router = new TeamLabAssetSpec(
            TeamLabAssetSpecKind.Docker,
            TopologyKey: "jump",
            Name: "Jump",
            SourceTemplateId: 42,
            Image: "registry.local/jump:latest",
            CpuCount: 10,
            MemoryLimit: 512,
            StorageLimit: 256,
            ExposePort: 22,
            InfrastructureRole: "Router",
            StartPriority: 20,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec("asset", "public", "tl12-public", "eth0", "10.90.0.3", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false),
                new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth1", "10.90.0.19", 28,
                    "02:42:ac:10:00:03", IsPrimary: false, RemoveDefaultRoute: false)
            ]);

        var config = TeamLabDeploymentService.BuildNativeDockerContainerConfig(router, teamId: 7,
            workerNodeId, flag: null);

        Assert.True(config.EnableNetworkAdmin);
        Assert.True(config.EnableIpForwarding);
    }

    [Fact]
    public void BuildRuntimeRouteMatrix_UsesPublishedRouteEdgesWithoutOpeningUnrelatedNetworks()
    {
        var service = new PenetrationNetwork { Id = 10, TopologyKey = "service", Name = "Service" };
        var business = new PenetrationNetwork { Id = 20, TopologyKey = "business", Name = "Business" };
        var data = new PenetrationNetwork { Id = 30, TopologyKey = "data", Name = "Data" };
        var portal = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "portal",
            Name = "Portal",
            NetworkId = service.Id,
            Network = service
        };
        var worker = new PenetrationNode
        {
            Id = 102,
            TopologyKey = "worker",
            Name = "Worker",
            NetworkId = business.Id,
            Network = business
        };
        var database = new PenetrationNode
        {
            Id = 103,
            TopologyKey = "database",
            Name = "Database",
            NetworkId = data.Id,
            Network = data
        };
        var config = new PenetrationConfig
        {
            Networks = [service, business, data],
            Nodes = [portal, worker, database],
            Edges =
            [
                new PenetrationEdge
                {
                    SourceKind = PenetrationPolicyScope.Node,
                    SourceId = portal.Id,
                    SourceNodeId = portal.Id,
                    TargetKind = PenetrationPolicyScope.Node,
                    TargetId = worker.Id,
                    TargetNodeId = worker.Id,
                    PolicyAction = PenetrationPolicyAction.Allow,
                    EnforcementMode = PenetrationEnforcementMode.Both
                }
            ]
        };
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("service", "Service", "10.90.0.0/28", "10.90.0.1", "tl12-service"),
            new TeamLabRuntimeNetworkSpec("business", "Business", "10.90.0.16/28", "10.90.0.17", "tl12-business"),
            new TeamLabRuntimeNetworkSpec("data", "Data", "10.90.0.32/28", "10.90.0.33", "tl12-data")
        };
        var assets = new[]
        {
            new TeamLabAssetSpec(TeamLabAssetSpecKind.Docker, "portal", "Portal", 42, "portal", 10, 512, 256, 8080,
                null, 50,
                [new TeamLabAssetInterfaceSpec("asset", "service", "tl12-service", "eth0", "10.90.0.3", 28, "02:42:ac:10:00:02", true, false)]),
            new TeamLabAssetSpec(TeamLabAssetSpecKind.Docker, "worker", "Worker", 43, "worker", 10, 512, 256, 8080,
                null, 50,
                [new TeamLabAssetInterfaceSpec("asset", "business", "tl12-business", "eth0", "10.90.0.19", 28, "02:42:ac:10:00:03", true, false)]),
            new TeamLabAssetSpec(TeamLabAssetSpecKind.Docker, "database", "Database", 44, "database", 10, 512, 256, 5432,
                null, 50,
                [new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth0", "10.90.0.35", 28, "02:42:ac:10:00:04", true, false)])
        };

        var matrix = TeamLabDeploymentService.BuildRuntimeRouteMatrix(config, networks, assets);

        Assert.True(matrix.AllowedCidrsByNetworkKey.TryGetValue("service", out var serviceRoutes));
        Assert.Contains("10.90.0.16/28", serviceRoutes);
        Assert.DoesNotContain("10.90.0.32/28", serviceRoutes);
    }

    [Fact]
    public void BuildPlayerNetworkAccess_OnlyExposesEntryNetworkToWireGuardPeer()
    {
        var entry = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "entry",
            Name = "Entry",
            IsEntry = true,
            OrderIndex = 10
        };
        var app = new PenetrationNetwork
        {
            Id = 20,
            TopologyKey = "app",
            Name = "App",
            OrderIndex = 20
        };
        var data = new PenetrationNetwork
        {
            Id = 30,
            TopologyKey = "data",
            Name = "Data",
            OrderIndex = 30
        };
        var config = new PenetrationConfig
        {
            Networks = [app, data, entry]
        };
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("app", "App", "10.90.0.16/28", "10.90.0.17", "tl12-app"),
            new TeamLabRuntimeNetworkSpec("data", "Data", "10.90.0.32/28", "10.90.0.33", "tl12-data"),
            new TeamLabRuntimeNetworkSpec("entry", "Entry", "10.90.0.0/28", "10.90.0.1", "tl12-entry")
        };

        var access = TeamLabDeploymentService.BuildPlayerNetworkAccess(config, networks);

        Assert.Equal(["10.90.0.0/28"], access.AllowedCidrs);
        Assert.Equal(["10.90.0.16/28", "10.90.0.32/28"], access.BlockedCidrs);
    }

    [Fact]
    public void BuildPlayerNetworkAccess_FallsBackToFirstTopologyNetworkWhenNoEntryFlagExists()
    {
        var app = new PenetrationNetwork
        {
            Id = 20,
            TopologyKey = "app",
            Name = "App",
            OrderIndex = 20
        };
        var entry = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "entry",
            Name = "Entry",
            OrderIndex = 10
        };
        var config = new PenetrationConfig
        {
            Networks = [app, entry]
        };
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("app", "App", "10.90.0.16/28", "10.90.0.17", "tl12-app"),
            new TeamLabRuntimeNetworkSpec("entry", "Entry", "10.90.0.0/28", "10.90.0.1", "tl12-entry")
        };

        var access = TeamLabDeploymentService.BuildPlayerNetworkAccess(config, networks);

        Assert.Equal(["10.90.0.0/28"], access.AllowedCidrs);
        Assert.Equal(["10.90.0.16/28"], access.BlockedCidrs);
    }

    [Fact]
    public void BuildDhcpDnsRequests_GeneratesStaticLeasesAndDnsRecordsPerNetwork()
    {
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("data", "Data", "10.90.0.16/28", "10.90.0.17", "tl12-data")
        };
        var assets = new[]
        {
            new TeamLabAssetSpec(
                TeamLabAssetSpecKind.Vm,
                TopologyKey: "win-ad",
                Name: "Windows AD",
                SourceTemplateId: 7,
                Image: "/images/win.qcow2",
                CpuCount: 20,
                MemoryLimit: 4096,
                StorageLimit: 40960,
                ExposePort: 3389,
                InfrastructureRole: "DomainController",
                StartPriority: 10,
                Interfaces:
                [
                    new TeamLabAssetInterfaceSpec("asset", "data", "tl12-data", "eth0", "10.90.0.19", 28,
                        "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
                ])
        };

        var requests = TeamLabDeploymentService.BuildDhcpDnsRequests(
            runtimeId: 12,
            routerNamespace: "tlr12",
            networks,
            assets,
            dryRun: false);

        var request = Assert.Single(requests);
        Assert.Equal("tl12-data", request.BridgeName);
        Assert.Equal("tlr12n0", request.InterfaceName);
        Assert.Equal("10.90.0.17", request.GatewayIp);
        Assert.Equal("teamlab12.local", request.Domain);
        Assert.Contains(request.Leases, lease =>
            lease.MacAddress == "02:42:ac:10:00:02" &&
            lease.IpAddress == "10.90.0.19" &&
            lease.Hostname == "win-ad");
        Assert.Contains(request.DnsRecords, record =>
            record.Hostname == "win-ad" && record.IpAddress == "10.90.0.19");
    }

    [Fact]
    public void BuildNativeCleanupResourceNames_UsesAllTrackedRuntimeNetworks()
    {
        var runtime = new TeamLabRuntime
        {
            Id = 123,
            Networks =
            [
                new TeamLabRuntimeNetwork { TopologyKey = "public", BridgeName = "tl123-public" },
                new TeamLabRuntimeNetwork { TopologyKey = "business", BridgeName = "tl123-business" },
                new TeamLabRuntimeNetwork { TopologyKey = "data", BridgeName = "tl123-data" }
            ]
        };

        var resources = TeamLabDeploymentService.BuildNativeCleanupResourceNames(runtime);

        Assert.Contains("tl123-public", resources);
        Assert.Contains("tl123-business", resources);
        Assert.Contains("tl123-data", resources);
        Assert.Contains("tlr123", resources);
        Assert.Contains("tlwg123", resources);
    }

    [Fact]
    public void BuildNativeCleanupResourceNames_UsesPlannedNetworksBeforeRuntimeFactsExist()
    {
        var names = TeamLabDeploymentService.BuildResourceNames(123, ["public", "business"]);
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("public", "Public", "10.180.1.0/28", "10.180.1.1", "tl123-public"),
            new TeamLabRuntimeNetworkSpec("business", "Business", "10.180.1.16/28", "10.180.1.17", "tl123-business")
        };

        var resources = TeamLabDeploymentService.BuildNativeCleanupResourceNames(names, networks);
        var serviceNames = TeamLabDeploymentService.BuildDhcpDnsRequests(123, names.RouterNamespace, networks, [], dryRun: false)
            .Select(request => request.ServiceName)
            .ToArray();

        Assert.Contains("tl123-public", resources);
        Assert.Contains("tl123-business", resources);
        Assert.All(serviceNames, service => Assert.Contains(service, resources));
        Assert.Contains(names.RouterNamespace, resources);
        Assert.Contains(names.WireGuardInterface, resources);
    }

    [Fact]
    public void BuildNativeCleanupResourceNames_MatchesDhcpDnsRequestServiceNames()
    {
        var names = TeamLabDeploymentService.BuildResourceNames(123, ["public", "business"]);
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("public", "Public", "10.180.1.0/28", "10.180.1.1", "tl123-public"),
            new TeamLabRuntimeNetworkSpec("business-zone", "Business", "10.180.1.16/28", "10.180.1.17", "tl123-business")
        };

        var resources = TeamLabDeploymentService.BuildNativeCleanupResourceNames(names, networks);
        var dhcpDnsServices = TeamLabDeploymentService.BuildDhcpDnsRequests(123, names.RouterNamespace, networks, [], dryRun: false)
            .Select(request => request.ServiceName);

        Assert.All(dhcpDnsServices, service => Assert.Contains(service, resources));
    }

    [Fact]
    public void RecordNativeRuntimeFacts_TracksDhcpDnsServicesForDestroyCleanup()
    {
        var runtime = new TeamLabRuntime { Id = 123 };
        var names = TeamLabDeploymentService.BuildResourceNames(123, ["public", "business"]);
        var networks = new[]
        {
            new TeamLabRuntimeNetworkSpec("public", "Public", "10.180.1.0/28", "10.180.1.1", "tl123-public"),
            new TeamLabRuntimeNetworkSpec("business", "Business", "10.180.1.16/28", "10.180.1.17", "tl123-business")
        };

        TeamLabDeploymentService.RecordNativeRuntimeFacts(runtime, names, networks);

        var resources = TeamLabDeploymentService.BuildNativeCleanupResourceNames(runtime);
        var serviceNames = TeamLabDeploymentService.BuildDhcpDnsRequests(123, names.RouterNamespace, networks, [], dryRun: false)
            .Select(request => request.ServiceName)
            .ToArray();

        Assert.All(serviceNames, service => Assert.Contains(service, resources));
        Assert.Contains(runtime.Assets, asset =>
            asset.Kind == TeamLabResourceKind.DhcpDnsService &&
            asset.RuntimeResourceId == serviceNames[0] &&
            asset.Status == TeamLabRuntimeStatus.Running);
    }

    [Fact]
    public void BuildNativeAssetCleanupPlan_UsesTrackedDockerAndVmRuntimeIds()
    {
        var runtime = new TeamLabRuntime
        {
            Assets =
            [
                new TeamLabRuntimeAsset
                {
                    Kind = TeamLabResourceKind.Docker,
                    RuntimeResourceId = "container-a",
                    Status = TeamLabRuntimeStatus.Running
                },
                new TeamLabRuntimeAsset
                {
                    Kind = TeamLabResourceKind.Vm,
                    RuntimeResourceId = "vm-a",
                    Status = TeamLabRuntimeStatus.Failed
                },
                new TeamLabRuntimeAsset
                {
                    Kind = TeamLabResourceKind.Docker,
                    RuntimeResourceId = "container-old",
                    Status = TeamLabRuntimeStatus.Destroyed
                },
                new TeamLabRuntimeAsset
                {
                    Kind = TeamLabResourceKind.RouterNamespace,
                    RuntimeResourceId = "tlr123",
                    Status = TeamLabRuntimeStatus.Running
                }
            ]
        };

        var plan = TeamLabDeploymentService.BuildNativeAssetCleanupPlan(runtime);

        Assert.Equal(["container-a"], plan.ContainerIds);
        Assert.Equal(["vm-a"], plan.VmNames);
    }

    [Fact]
    public async Task DeployQueuedRuntimeAsync_CreatesIndependentDockerAssetsConcurrently()
    {
        await using var context = CreateContext();
        var nodeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        await SeedTwoDockerTeamLabRuntimeAsync(context, nodeId);
        var agent = new BlockingTeamLabAgentClient(expectedContainerCreates: 2);
        var service = CreateDeploymentService(context, agent);

        var deployTask = service.DeployQueuedRuntimeAsync(runtimeId: 1, CancellationToken.None);

        var bothCreatesStarted = await Task.WhenAny(agent.BothContainerCreatesStarted.Task,
            Task.Delay(TimeSpan.FromSeconds(3)));
        if (!ReferenceEquals(agent.BothContainerCreatesStarted.Task, bothCreatesStarted))
        {
            agent.ReleaseContainerCreates();
            var earlyResult = await deployTask;
            Assert.Fail($"Expected two Docker asset creates to start concurrently. " +
                        $"Observed {agent.ContainerCreateCount} create call(s). " +
                        $"Deployment result: {earlyResult.Success} {earlyResult.Message}");
        }

        agent.ReleaseContainerCreates();
        var result = await deployTask;

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, agent.MaxConcurrentContainerCreates);
        Assert.Equal(2, await context.TeamLabRuntimeAssets.CountAsync(a =>
            a.RuntimeId == 1 && a.Kind == TeamLabResourceKind.Docker));
    }

    [Fact]
    public async Task DeployQueuedRuntimeAsync_CleansAlreadyCreatedParallelAssetsWhenOneAssetFails()
    {
        await using var context = CreateContext();
        var nodeId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await SeedTwoDockerTeamLabRuntimeAsync(context, nodeId);
        var agent = new OneFailsAfterParallelStartTeamLabAgentClient(expectedContainerCreates: 2);
        var service = CreateDeploymentService(context, agent);

        var result = await service.DeployQueuedRuntimeAsync(runtimeId: 1, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("simulated create failure", result.Message);
        Assert.Contains(agent.DestroyedContainers,
            containerId => containerId.StartsWith("container-", StringComparison.Ordinal));
    }

    private static DockerImageRegistryService CreateDockerRegistryService(string address)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var agentClient = new AgentClient(
            new StaticHttpClientFactory(),
            scopeFactory,
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance);

        return new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings { Address = address }),
            scopeFactory,
            agentClient,
            NullLogger<DockerImageRegistryService>.Instance);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static TeamLabDeploymentService CreateDeploymentService(AppDbContext context, AgentClient? agentClient = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        agentClient ??= new AgentClient(
            new StaticHttpClientFactory(),
            scopeFactory,
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance);
        var planService = new TeamLabPlanService(
            context,
            Options.Create(new TeamLabNetworkConfig()),
            NullLogger<TeamLabPlanService>.Instance);
        var registry = CreateDockerRegistryService("10.24.0.28:5000");
        var lockService = new GZCTF.Services.Concurrency.LocalSemaphoreLock(
            NullLogger<GZCTF.Services.Concurrency.LocalSemaphoreLock>.Instance);
        var capacity = new FleetCapacityReservationService(
            context,
            lockService,
            NullLogger<FleetCapacityReservationService>.Instance);

        return new TeamLabDeploymentService(
            context,
            planService,
            agentClient,
            registry,
            new TeamLabWireGuardService(
                new EphemeralDataProtectionProvider(),
                Options.Create(new PublicUdpGatewayConfig { PublicEndpoint = "203.195.157.191" }),
                Options.Create(new ContainerProvider { PublicEntry = "203.195.157.191" })),
            new RecordingPublicUdpGatewayProvider(),
            Options.Create(new TeamLabNetworkConfig()),
            capacity,
            new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance),
            NullLogger<TeamLabDeploymentService>.Instance);
    }

    private static async Task SeedTwoDockerTeamLabRuntimeAsync(AppDbContext context, Guid nodeId)
    {
        var game = new Game
        {
            Id = 1,
            Title = "TeamLab",
            GameType = GameType.Penetration,
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            EndTimeUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var team = new Team { Id = 1, Name = "team-1" };
        var templateA = new ImageTemplate
        {
            Id = 101,
            Name = "portal",
            ImageType = ImageType.Docker,
            RegistryUrl = "registry.local/portal:latest",
            Status = ImageStatus.Ready
        };
        var templateB = new ImageTemplate
        {
            Id = 102,
            Name = "api",
            ImageType = ImageType.Docker,
            RegistryUrl = "registry.local/api:latest",
            Status = ImageStatus.Ready
        };
        context.Games.Add(game);
        context.Teams.Add(team);
        context.Participations.Add(new Participation
        {
            GameId = game.Id,
            TeamId = team.Id,
            Status = ParticipationStatus.Accepted
        });
        context.WorkerNodes.Add(new WorkerNode
        {
            Id = nodeId,
            Name = "teamlab-node",
            HostAddress = "10.24.0.30",
            AuthToken = "token",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            MaxContainers = 8,
            MaxVms = 2,
            CurrentContainers = 2,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.2"
        });
        context.ImageTemplates.AddRange(templateA, templateB);
        context.PenetrationConfigs.Add(new PenetrationConfig
        {
            Id = 1,
            GameId = game.Id,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            PublishedVersion = 1,
            Status = PenetrationDeploymentStatus.Published
        });
        context.PenetrationPublishedSnapshots.Add(new PenetrationPublishedSnapshot
        {
            Id = 1,
            GameId = game.Id,
            PublishedVersion = 1,
            SnapshotHash = "hash",
            SnapshotJson = BuildTwoDockerSnapshotJson(templateA.Id, templateB.Id)
        });
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            Id = 1,
            GameId = game.Id,
            TeamId = team.Id,
            PublishedVersion = 1,
            WorkerNodeId = nodeId,
            NetworkPrefix = "10.90.1.0/24",
            Status = TeamLabRuntimeStatus.Scheduled,
            PublicUdpMapping = new TeamLabPublicUdpMapping
            {
                RuntimeId = 1,
                PublicUdpPort = 32001,
                WorkerWireGuardPort = 51821,
                WorkerTunnelIp = "10.250.0.2"
            }
        });
        await context.SaveChangesAsync();
    }

    private static string BuildTwoDockerSnapshotJson(int templateAId, int templateBId) =>
        JsonSerializer.Serialize(new PenetrationConfigModel
        {
            GameId = 1,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            PublishedVersion = 1,
            Status = PenetrationDeploymentStatus.Published,
            Networks =
            [
                new PenetrationNetworkModel
                {
                    Id = 1,
                    TopologyKey = "entry",
                    Name = "Entry",
                    OrderIndex = 0
                }
            ],
            Nodes =
            [
                new PenetrationNodeModel
                {
                    Id = 1,
                    TopologyKey = "portal",
                    Name = "Portal",
                    NetworkId = 1,
                    ImageTemplateId = templateAId,
                    CpuCount = 1,
                    MemoryLimit = 128,
                    StorageLimit = 128,
                    ExposePort = 80,
                    OrderIndex = 10,
                    Interfaces =
                    [
                        new PenetrationInterfaceModel
                        {
                            Id = 1,
                            NodeId = 1,
                            NetworkId = 1,
                            TopologyKey = "portal-eth0",
                            Name = "eth0",
                            IsPrimary = true,
                            OrderIndex = 0
                        }
                    ]
                },
                new PenetrationNodeModel
                {
                    Id = 2,
                    TopologyKey = "api",
                    Name = "API",
                    NetworkId = 1,
                    ImageTemplateId = templateBId,
                    CpuCount = 1,
                    MemoryLimit = 128,
                    StorageLimit = 128,
                    ExposePort = 8080,
                    OrderIndex = 10,
                    Interfaces =
                    [
                        new PenetrationInterfaceModel
                        {
                            Id = 2,
                            NodeId = 2,
                            NetworkId = 1,
                            TopologyKey = "api-eth0",
                            Name = "eth0",
                            IsPrimary = true,
                            OrderIndex = 0
                        }
                    ]
                }
            ]
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class RecordingPublicUdpGatewayProvider : IPublicUdpGatewayProvider
    {
        public Task<PublicUdpGatewaySyncResult> SyncMappingAsync(TeamLabPublicUdpMapping mapping,
            CancellationToken token) =>
            Task.FromResult(new PublicUdpGatewaySyncResult(true, "synced", []));

        public Task<PublicUdpGatewaySyncResult> RemoveMappingAsync(TeamLabPublicUdpMapping mapping,
            CancellationToken token) =>
            Task.FromResult(new PublicUdpGatewaySyncResult(true, "removed", []));
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private abstract class TestTeamLabAgentClientBase : AgentClient
    {
        protected TestTeamLabAgentClientBase()
            : base(new StaticHttpClientFactory(),
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new ConfigurationBuilder().Build(),
                NullLogger<AgentClient>.Instance)
        {
        }

        static TeamLabDryRunResponse Ok(string message) => new(true, false, message, []);

        public override Task<TeamLabDryRunResponse?> CreateTeamLabBridgeAsync(Guid nodeId,
            TeamLabBridgeRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("bridge"));

        public override Task<TeamLabDryRunResponse?> CreateTeamLabRouterAsync(Guid nodeId,
            TeamLabRouterRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("router"));

        public override Task<TeamLabDryRunResponse?> ConfigureTeamLabWireGuardAsync(Guid nodeId,
            TeamLabWireGuardRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("wireguard"));

        public override Task<TeamLabDryRunResponse?> ConfigureTeamLabDhcpDnsAsync(Guid nodeId,
            TeamLabDhcpDnsRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("dhcp"));

        public override Task<TeamLabDryRunResponse?> ProbeTeamLabDhcpDnsAsync(Guid nodeId,
            TeamLabDhcpDnsProbeRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("dns"));

        public override Task<TeamLabDryRunResponse?> AttachTeamLabContainerAsync(Guid nodeId,
            TeamLabContainerAttachRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("attach"));

        public override Task<TeamLabDryRunResponse?> ProbeTeamLabAsync(Guid nodeId,
            TeamLabProbeRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("probe"));

        public override Task<TeamLabDryRunResponse?> CleanupTeamLabAsync(Guid nodeId,
            TeamLabCleanupRequest request, CancellationToken token) =>
            Task.FromResult<TeamLabDryRunResponse?>(Ok("cleanup"));
    }

    private sealed class BlockingTeamLabAgentClient : TestTeamLabAgentClientBase
    {
        readonly int _expectedContainerCreates;
        readonly TaskCompletionSource _releaseContainerCreates =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _activeContainerCreates;
        int _containerCreateCount;

        public BlockingTeamLabAgentClient(int expectedContainerCreates)
        {
            _expectedContainerCreates = expectedContainerCreates;
        }

        public TaskCompletionSource BothContainerCreatesStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentContainerCreates { get; private set; }
        public int ContainerCreateCount => Volatile.Read(ref _containerCreateCount);

        public void ReleaseContainerCreates() => _releaseContainerCreates.TrySetResult();

        public override async Task<AgentCreateContainerResponse> CreateContainerOrThrowAsync(Guid nodeId,
            ContainerConfig config, CancellationToken token)
        {
            var active = Interlocked.Increment(ref _activeContainerCreates);
            MaxConcurrentContainerCreates = Math.Max(MaxConcurrentContainerCreates, active);
            if (Interlocked.Increment(ref _containerCreateCount) == _expectedContainerCreates)
                BothContainerCreatesStarted.TrySetResult();

            try
            {
                await _releaseContainerCreates.Task.WaitAsync(token);
                return new AgentCreateContainerResponse
                {
                    ContainerId = $"container-{config.Image}",
                    Port = config.ExposedPort
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeContainerCreates);
            }
        }
    }

    private sealed class OneFailsAfterParallelStartTeamLabAgentClient : TestTeamLabAgentClientBase
    {
        readonly int _expectedContainerCreates;
        readonly TaskCompletionSource _allCreatesStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _containerCreateCount;

        public OneFailsAfterParallelStartTeamLabAgentClient(int expectedContainerCreates)
        {
            _expectedContainerCreates = expectedContainerCreates;
        }

        public List<string> DestroyedContainers { get; } = [];

        public override async Task<AgentCreateContainerResponse> CreateContainerOrThrowAsync(Guid nodeId,
            ContainerConfig config, CancellationToken token)
        {
            var count = Interlocked.Increment(ref _containerCreateCount);
            if (count == _expectedContainerCreates)
                _allCreatesStarted.TrySetResult();

            await _allCreatesStarted.Task.WaitAsync(token);
            if (config.Image.Contains("api", StringComparison.OrdinalIgnoreCase))
                throw new AgentClientException("simulated create failure");

            return new AgentCreateContainerResponse
            {
                ContainerId = "container-created-before-failure",
                Port = config.ExposedPort
            };
        }

        public override Task DestroyContainerAsync(Guid nodeId, string containerId, CancellationToken token)
        {
            DestroyedContainers.Add(containerId);
            return Task.CompletedTask;
        }
    }

}
