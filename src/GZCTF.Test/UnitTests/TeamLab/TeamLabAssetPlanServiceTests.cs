using System;
using System.Collections.Generic;
using System.Linq;
using GZCTF.Models.Data;
using GZCTF.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabAssetPlanServiceTests
{
    [Fact]
    public void BuildMacAddress_IsStableAndLocallyAdministered()
    {
        var mac = TeamLabAssetPlanService.BuildMacAddress(12, "portal", "eth0");

        Assert.Equal(mac, TeamLabAssetPlanService.BuildMacAddress(12, "portal", "eth0"));
        Assert.StartsWith("02:42:", mac);
        Assert.Matches("^([0-9a-f]{2}:){5}[0-9a-f]{2}$", mac);
    }

    [Fact]
    public void BuildContainerAttachRequest_UsesTeamLabVethWithoutFabricPath()
    {
        var iface = new TeamLabAssetInterfaceSpec(
            NetworkKey: "dmz",
            BridgeName: "tl12-dmz",
            InterfaceName: "eth0",
            IpAddress: "10.180.1.10",
            PrefixLength: 24,
            MacAddress: "02:42:ac:10:00:02",
            IsPrimary: true,
            RemoveDefaultRoute: true);

        var request = TeamLabAssetPlanService.BuildContainerAttachRequest(12, "container-1", iface, dryRun: false);

        Assert.Equal(12, request.RuntimeId);
        Assert.Equal("container-1", request.ContainerId);
        Assert.Equal("tl12-dmz", request.BridgeName);
        Assert.Equal("eth0", request.ContainerInterfaceName);
        Assert.Equal("10.180.1.10/24", request.AddressCidr);
        Assert.Equal("02:42:ac:10:00:02", request.MacAddress);
        Assert.True(request.RemoveDefaultRoute);
        Assert.False(request.DryRun);
        Assert.DoesNotContain("fabric", request.HostInterfaceName);
    }

    [Fact]
    public void BuildVmSpec_RejectsDockerTemplate()
    {
        Assert.Throws<ArgumentException>(() => TeamLabAssetPlanService.BuildVmSpec(
            new PenetrationNode { TopologyKey = "win", Name = "Windows" },
            new ImageTemplate { Id = 1, Name = "docker", ImageType = ImageType.Docker },
            []));
    }

    [Fact]
    public void BuildPublishedAssetPlan_MapsDockerAndVmAssetsToStableTeamLabInterfaces()
    {
        var publicNetwork = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "public",
            Name = "Public",
            Slug = "public",
            IsEntry = true,
            ZoneType = PenetrationZoneType.Public,
            OrderIndex = 0
        };
        var dataNetwork = new PenetrationNetwork
        {
            Id = 20,
            TopologyKey = "data",
            Name = "Data",
            Slug = "data",
            ZoneType = PenetrationZoneType.Data,
            OrderIndex = 1
        };
        var portal = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "portal",
            Name = "Portal",
            Network = publicNetwork,
            NetworkId = publicNetwork.Id,
            ImageTemplateId = 1,
            ExposePort = 8080,
            MemoryLimit = 512,
            CpuCount = 10,
            StorageLimit = 256,
            OrderIndex = 0
        };
        var windows = new PenetrationNode
        {
            Id = 102,
            TopologyKey = "win-ad",
            Name = "Windows AD",
            Network = dataNetwork,
            NetworkId = dataNetwork.Id,
            ReservedAdRole = "DomainController",
            ImageTemplateId = 2,
            MemoryLimit = 4096,
            CpuCount = 20,
            StorageLimit = 40960,
            OrderIndex = 1
        };
        portal.Interfaces.Add(new PenetrationInterface
        {
            Id = 1001,
            TopologyKey = "portal-eth0",
            Node = portal,
            NodeId = portal.Id,
            Network = publicNetwork,
            NetworkId = publicNetwork.Id,
            Name = "eth0",
            IsPrimary = true
        });
        windows.Interfaces.Add(new PenetrationInterface
        {
            Id = 1002,
            TopologyKey = "win-eth0",
            Node = windows,
            NodeId = windows.Id,
            Network = dataNetwork,
            NetworkId = dataNetwork.Id,
            Name = "eth0",
            IsPrimary = true
        });
        var config = new PenetrationConfig
        {
            Id = 7,
            GameId = 5,
            PublishedVersion = 3,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks = [publicNetwork, dataNetwork],
            Nodes = [portal, windows]
        };

        var result = TeamLabAssetPlanService.BuildPublishedAssetPlan(config, runtimeId: 12, teamIndex: 0,
            templates: new Dictionary<int, ImageTemplate>
            {
                [1] = new() { Id = 1, Name = "portal", RegistryUrl = "registry.local/portal:latest", ImageType = ImageType.Docker, OSType = OSType.Linux, Status = ImageStatus.Ready },
                [2] = new() { Id = 2, Name = "win2022", LocalFilePath = "/images/win2022.qcow2", ImageType = ImageType.Qcow2, OSType = OSType.Windows, Status = ImageStatus.Ready }
            });

        Assert.True(result.Success, result.Message);
        Assert.Collection(result.Networks,
            network =>
            {
                Assert.Equal("public", network.TopologyKey);
                Assert.Equal("10.90.0.0/28", network.Cidr);
                Assert.Equal("tl12-public", network.BridgeName);
            },
            network =>
            {
                Assert.Equal("data", network.TopologyKey);
                Assert.Equal("10.90.0.16/28", network.Cidr);
                Assert.Equal("tl12-data", network.BridgeName);
            });
        var assetsByKey = result.Assets.ToDictionary(asset => asset.TopologyKey);
        Assert.True(result.Assets.Single(a => a.TopologyKey == "win-ad").StartPriority <
                    result.Assets.Single(a => a.TopologyKey == "portal").StartPriority);
        Assert.Collection([assetsByKey["portal"], assetsByKey["win-ad"]],
            asset =>
            {
                Assert.Equal(TeamLabAssetSpecKind.Docker, asset.Kind);
                Assert.Equal("registry.local/portal:latest", asset.Image);
                var iface = Assert.Single(asset.Interfaces);
                Assert.Equal("10.90.0.3", iface.IpAddress);
                Assert.Equal("tl12-public", iface.BridgeName);
            },
            asset =>
            {
                Assert.Equal(TeamLabAssetSpecKind.Vm, asset.Kind);
                Assert.Equal("/images/win2022.qcow2", asset.Image);
                Assert.Equal("DomainController", asset.InfrastructureRole);
                Assert.Equal(10, asset.StartPriority);
                var iface = Assert.Single(asset.Interfaces);
                Assert.Equal("10.90.0.19", iface.IpAddress);
                Assert.Equal("tl12-data", iface.BridgeName);
            });
    }

    [Fact]
    public void BuildPublishedAssetPlan_CombinesDockerRegistryPrefixWithTemplateName()
    {
        var network = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "service",
            Name = "Service",
            Slug = "service",
            IsEntry = true,
            OrderIndex = 0
        };
        var node = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "busybox",
            Name = "BusyBox",
            Network = network,
            NetworkId = network.Id,
            ImageTemplateId = 3,
            OrderIndex = 0
        };
        var config = new PenetrationConfig
        {
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks = [network],
            Nodes = [node]
        };

        var result = TeamLabAssetPlanService.BuildPublishedAssetPlan(config, runtimeId: 12, teamIndex: 0,
            templates: new Dictionary<int, ImageTemplate>
            {
                [3] = new()
                {
                    Id = 3,
                    Name = "busybox:latest",
                    RegistryUrl = "docker.io/library",
                    ImageType = ImageType.Docker,
                    OSType = OSType.Linux,
                    Status = ImageStatus.Ready
                }
            });

        Assert.True(result.Success, result.Message);
        Assert.Equal("docker.io/library/busybox:latest", Assert.Single(result.Assets).Image);
    }

    [Fact]
    public void BuildPublishedAssetPlan_OrdersDomainControllerBeforeDomainMembers()
    {
        var network = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "data",
            Name = "Data",
            Slug = "data",
            ZoneType = PenetrationZoneType.Data,
            OrderIndex = 0
        };
        var member = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "win-client",
            Name = "Windows Client",
            Network = network,
            NetworkId = network.Id,
            ImageTemplateId = 1,
            ReservedAdRole = "DomainMember",
            OrderIndex = 0
        };
        var controller = new PenetrationNode
        {
            Id = 102,
            TopologyKey = "dc01",
            Name = "Domain Controller",
            Network = network,
            NetworkId = network.Id,
            ImageTemplateId = 1,
            ReservedAdRole = "DomainController",
            OrderIndex = 1
        };
        var config = new PenetrationConfig
        {
            Id = 7,
            GameId = 5,
            PublishedVersion = 3,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks = [network],
            Nodes = [member, controller]
        };

        var result = TeamLabAssetPlanService.BuildPublishedAssetPlan(config, runtimeId: 12, teamIndex: 0,
            templates: new Dictionary<int, ImageTemplate>
            {
                [1] = new() { Id = 1, Name = "win2022", LocalFilePath = "/images/win2022.qcow2", ImageType = ImageType.Qcow2, OSType = OSType.Windows, Status = ImageStatus.Ready }
            });

        Assert.True(result.Success, result.Message);
        Assert.Collection(result.Assets,
            asset =>
            {
                Assert.Equal("dc01", asset.TopologyKey);
                Assert.Equal("DomainController", asset.InfrastructureRole);
                Assert.Equal(10, asset.StartPriority);
            },
            asset =>
            {
                Assert.Equal("win-client", asset.TopologyKey);
                Assert.Equal("DomainMember", asset.InfrastructureRole);
                Assert.Equal(30, asset.StartPriority);
            });
    }

    [Fact]
    public void BuildPublishedAssetPlan_RejectsNodeWithoutReadyImageTemplate()
    {
        var network = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "public",
            Name = "Public",
            Slug = "public",
            IsEntry = true,
            ZoneType = PenetrationZoneType.Public,
            OrderIndex = 0
        };
        var node = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "missing",
            Name = "Missing",
            Network = network,
            NetworkId = network.Id,
            OrderIndex = 0
        };
        var config = new PenetrationConfig
        {
            Id = 7,
            GameId = 5,
            PublishedVersion = 3,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks = [network],
            Nodes = [node]
        };

        var result = TeamLabAssetPlanService.BuildPublishedAssetPlan(config, runtimeId: 12, teamIndex: 0,
            templates: new Dictionary<int, ImageTemplate>());

        Assert.False(result.Success);
        Assert.Contains("Missing", result.Message);
        Assert.Contains("ready image template", result.Message);
    }

    [Fact]
    public void BuildPublishedAssetPlan_ReservesGatewayAndVpnClientAddresses()
    {
        var network = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "public",
            Name = "Public",
            Slug = "public",
            IsEntry = true,
            ZoneType = PenetrationZoneType.Public,
            OrderIndex = 0
        };
        var node = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "portal",
            Name = "Portal",
            Network = network,
            NetworkId = network.Id,
            ImageTemplateId = 1,
            OrderIndex = 0
        };
        var config = new PenetrationConfig
        {
            Id = 7,
            GameId = 5,
            PublishedVersion = 3,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks = [network],
            Nodes = [node]
        };

        var result = TeamLabAssetPlanService.BuildPublishedAssetPlan(config, runtimeId: 12, teamIndex: 0,
            templates: new Dictionary<int, ImageTemplate>
            {
                [1] = new() { Id = 1, Name = "portal", RegistryUrl = "registry.local/portal:latest", ImageType = ImageType.Docker, OSType = OSType.Linux, Status = ImageStatus.Ready }
            });

        Assert.True(result.Success, result.Message);
        Assert.Equal("10.90.0.1", Assert.Single(result.Networks).GatewayIp);
        var ip = Assert.Single(Assert.Single(result.Assets).Interfaces).IpAddress;
        Assert.NotEqual("10.90.0.1", ip);
        Assert.NotEqual("10.90.0.2", ip);
        Assert.Equal("10.90.0.3", ip);
    }

    [Fact]
    public void BuildPublishedAssetPlan_CanUseRuntimeTeamCidrAsAddressFactSource()
    {
        var network = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "public",
            Name = "Public",
            Slug = "public",
            IsEntry = true,
            ZoneType = PenetrationZoneType.Public,
            OrderIndex = 0
        };
        var node = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "portal",
            Name = "Portal",
            Network = network,
            NetworkId = network.Id,
            ImageTemplateId = 1,
            OrderIndex = 0
        };
        var config = new PenetrationConfig
        {
            Id = 7,
            GameId = 5,
            PublishedVersion = 3,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks = [network],
            Nodes = [node]
        };

        var result = TeamLabAssetPlanService.BuildPublishedAssetPlan(config, runtimeId: 12, teamIndex: 0,
            templates: new Dictionary<int, ImageTemplate>
            {
                [1] = new() { Id = 1, Name = "portal", RegistryUrl = "registry.local/portal:latest", ImageType = ImageType.Docker, OSType = OSType.Linux, Status = ImageStatus.Ready }
            },
            runtimeTeamCidr: "10.180.12.0/24");

        Assert.True(result.Success, result.Message);
        Assert.Equal("10.180.12.0/28", Assert.Single(result.Networks).Cidr);
        Assert.Equal("10.180.12.3", Assert.Single(Assert.Single(result.Assets).Interfaces).IpAddress);
    }

    [Fact]
    public void BuildPublishedAssetPlan_RuntimeTeamCidrOverridesPublishedSampleNetworkCidrs()
    {
        var network = new PenetrationNetwork
        {
            Id = 10,
            TopologyKey = "public",
            Name = "Public",
            Slug = "public",
            Cidr = "10.60.0.0/28",
            IsEntry = true,
            ZoneType = PenetrationZoneType.Public,
            OrderIndex = 0
        };
        var node = new PenetrationNode
        {
            Id = 101,
            TopologyKey = "portal",
            Name = "Portal",
            Network = network,
            NetworkId = network.Id,
            ImageTemplateId = 1,
            OrderIndex = 0
        };
        var config = new PenetrationConfig
        {
            Id = 7,
            GameId = 5,
            PublishedVersion = 3,
            BaseCidr = "10.60.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks = [network],
            Nodes = [node]
        };

        var result = TeamLabAssetPlanService.BuildPublishedAssetPlan(config, runtimeId: 12, teamIndex: 0,
            templates: new Dictionary<int, ImageTemplate>
            {
                [1] = new() { Id = 1, Name = "portal", RegistryUrl = "registry.local/portal:latest", ImageType = ImageType.Docker, OSType = OSType.Linux, Status = ImageStatus.Ready }
            },
            runtimeTeamCidr: "10.180.12.0/24");

        Assert.True(result.Success, result.Message);
        Assert.Equal("10.180.12.0/28", Assert.Single(result.Networks).Cidr);
        Assert.Equal("10.180.12.3", Assert.Single(Assert.Single(result.Assets).Interfaces).IpAddress);
    }
}
