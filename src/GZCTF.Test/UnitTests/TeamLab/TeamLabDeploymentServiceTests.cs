using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Internal;
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
                new TeamLabAssetInterfaceSpec("public", "tl12-public", "eth0", "10.90.0.2", 28,
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
                new TeamLabAssetInterfaceSpec("service", "tl12-service", "eth0", "10.90.0.3", 28,
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
                new TeamLabAssetInterfaceSpec("public", "tl12-public", "eth0", "10.90.0.2", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false),
                new TeamLabAssetInterfaceSpec("data", "tl12-data", "eth1", "10.90.0.18", 28,
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
                new TeamLabAssetInterfaceSpec("data", "tl12-data", "eth0", "10.90.0.19", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false),
                new TeamLabAssetInterfaceSpec("ops", "tl12-ops", "eth1", "10.90.0.35", 28,
                    "02:42:ac:10:00:03", IsPrimary: false, RemoveDefaultRoute: false)
            ]);

        var request = TeamLabDeploymentService.BuildNativeVmRequest(runtimeId: 12, spec, flag: "flag{win}");

        Assert.Equal(7, request.TemplateId);
        Assert.Equal("tl12-win-ad", request.VmName);
        Assert.Equal(4096, request.Memory);
        Assert.Equal(20, request.Cpu);
        Assert.Equal("flag{win}", request.Flag);
        Assert.Collection(request.Interfaces,
            iface =>
            {
                Assert.Equal("tl12-data", iface.BridgeName);
                Assert.Equal("02:42:ac:10:00:02", iface.MacAddress);
                Assert.Equal("virtio", iface.Model);
            },
            iface =>
            {
                Assert.Equal("tl12-ops", iface.BridgeName);
                Assert.Equal("02:42:ac:10:00:03", iface.MacAddress);
                Assert.Equal("virtio", iface.Model);
            });
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
                new TeamLabAssetInterfaceSpec("data", "tl12-data", "eth0", "10.90.0.19", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false)
            ]);

        var result = TeamLabDeploymentService.ValidateNativeVmReady(spec,
            new AgentVmIpResponse { VmName = "tl12-win-ad", IpAddress = actualIp, Status = status });

        Assert.Equal(expected, result.Success);
        if (!expected)
            Assert.Contains("Windows AD", result.Message);
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
                new TeamLabAssetInterfaceSpec("public", "tl12-public", "eth0", "10.90.0.3", 28,
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
                new TeamLabAssetInterfaceSpec("public", "tl12-public", "eth0", "10.90.0.3", 28,
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
                new TeamLabAssetInterfaceSpec("public", "tl12-public", "eth0", "10.90.0.3", 28,
                    "02:42:ac:10:00:02", IsPrimary: true, RemoveDefaultRoute: false),
                new TeamLabAssetInterfaceSpec("data", "tl12-data", "eth1", "10.90.0.19", 28,
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
                [new TeamLabAssetInterfaceSpec("service", "tl12-service", "eth0", "10.90.0.3", 28, "02:42:ac:10:00:02", true, false)]),
            new TeamLabAssetSpec(TeamLabAssetSpecKind.Docker, "worker", "Worker", 43, "worker", 10, 512, 256, 8080,
                null, 50,
                [new TeamLabAssetInterfaceSpec("business", "tl12-business", "eth0", "10.90.0.19", 28, "02:42:ac:10:00:03", true, false)]),
            new TeamLabAssetSpec(TeamLabAssetSpecKind.Docker, "database", "Database", 44, "database", 10, 512, 256, 5432,
                null, 50,
                [new TeamLabAssetInterfaceSpec("data", "tl12-data", "eth0", "10.90.0.35", 28, "02:42:ac:10:00:04", true, false)])
        };

        var matrix = TeamLabDeploymentService.BuildRuntimeRouteMatrix(config, networks, assets);

        Assert.True(matrix.AllowedCidrsByNetworkKey.TryGetValue("service", out var serviceRoutes));
        Assert.Contains("10.90.0.16/28", serviceRoutes);
        Assert.DoesNotContain("10.90.0.32/28", serviceRoutes);
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
                    new TeamLabAssetInterfaceSpec("data", "tl12-data", "eth0", "10.90.0.19", 28,
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

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

}
