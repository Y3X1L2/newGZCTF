using System.Reflection;
using System.Collections.Generic;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class PenetrationServiceTopologyMappingTests
{
    [Fact]
    public void ApplyModelToConfig_PreservesVisualNodeEndpointsForNetworkScopedRouteEdges()
    {
        var config = new PenetrationConfig { Id = 7, GameId = 61 };
        var model = new PenetrationConfigModel
        {
            GameId = 61,
            Networks =
            [
                new PenetrationNetworkModel { Id = 92, TopologyKey = "net-edge", Name = "Edge", Slug = "edge", OrderIndex = 0 },
                new PenetrationNetworkModel { Id = 93, TopologyKey = "net-app", Name = "App", Slug = "app", OrderIndex = 1 }
            ],
            Nodes =
            [
                new PenetrationNodeModel
                {
                    Id = 106,
                    TopologyKey = "edge-router",
                    NetworkId = 92,
                    Name = "Edge Router",
                    OrderIndex = 0,
                    Interfaces =
                    [
                        new PenetrationInterfaceModel
                        {
                            Id = 322,
                            TopologyKey = "edge-router-eth0",
                            NodeId = 106,
                            NetworkId = 92,
                            Name = "eth0",
                            IsPrimary = true
                        }
                    ]
                },
                new PenetrationNodeModel
                {
                    Id = 108,
                    TopologyKey = "app-api",
                    NetworkId = 93,
                    Name = "App API",
                    OrderIndex = 1,
                    Interfaces =
                    [
                        new PenetrationInterfaceModel
                        {
                            Id = 324,
                            TopologyKey = "app-api-eth0",
                            NodeId = 108,
                            NetworkId = 93,
                            Name = "eth0",
                            IsPrimary = true
                        }
                    ]
                }
            ],
            Edges =
            [
                new PenetrationEdgeModel
                {
                    Id = 50,
                    TopologyKey = "edge-to-app",
                    SourceNodeId = 106,
                    TargetNodeId = 108,
                    SourceKind = PenetrationPolicyScope.Network,
                    SourceId = 92,
                    TargetKind = PenetrationPolicyScope.Network,
                    TargetId = 93,
                    Protocol = PenetrationProtocol.Any,
                    PortRange = "any",
                    PolicyAction = PenetrationPolicyAction.Allow,
                    EnforcementMode = PenetrationEnforcementMode.RuntimeRoute,
                    Priority = 100,
                    Label = "Edge -> App"
                }
            ]
        };

        ApplyModelToConfig(config, model);

        var edge = Assert.Single(config.Edges);
        Assert.Equal(PenetrationPolicyScope.Network, edge.SourceKind);
        Assert.Equal(PenetrationPolicyScope.Network, edge.TargetKind);
        Assert.Equal(92, edge.SourceId);
        Assert.Equal(93, edge.TargetId);
        Assert.Equal(106, edge.SourceNodeId);
        Assert.Equal(108, edge.TargetNodeId);
    }

    [Fact]
    public void BuildNetworkSubnets_PreservesExplicitMixedRfc1918CidrsAndAutoAllocatesBlankCidrs()
    {
        var config = new PenetrationConfig
        {
            BaseCidr = "10.190.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            Networks =
            [
                new PenetrationNetwork { Id = 10, TopologyKey = "entry", Name = "Entry", Slug = "entry", Cidr = "10.10.10.0/24", OrderIndex = 0 },
                new PenetrationNetwork { Id = 20, TopologyKey = "core", Name = "Core", Slug = "core", Cidr = "192.168.20.0/24", OrderIndex = 1 },
                new PenetrationNetwork { Id = 30, TopologyKey = "data", Name = "Data", Slug = "data", Cidr = "172.16.30.0/24", OrderIndex = 2 },
                new PenetrationNetwork { Id = 40, TopologyKey = "ops", Name = "Ops", Slug = "ops", OrderIndex = 3 }
            ]
        };
        var names = new Dictionary<int, string>
        {
            [10] = "entry",
            [20] = "core",
            [30] = "data",
            [40] = "ops"
        };

        var subnets = BuildNetworkSubnets(config, teamIndex: 2, names);

        Assert.Equal("10.10.10.0/24", subnets["entry"]);
        Assert.Equal("192.168.20.0/24", subnets["core"]);
        Assert.Equal("172.16.30.0/24", subnets["data"]);
        Assert.Equal("10.190.2.48/28", subnets["ops"]);
    }

    static void ApplyModelToConfig(PenetrationConfig config, PenetrationConfigModel model)
    {
        var method = typeof(PenetrationService).GetMethod(
            "ApplyModelToConfig",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(null, [config, model, true, true]);
    }

    static Dictionary<string, string> BuildNetworkSubnets(PenetrationConfig config, int teamIndex,
        Dictionary<int, string> networkNames)
    {
        var method = typeof(PenetrationService).GetMethod(
            "BuildNetworkSubnets",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return Assert.IsType<Dictionary<string, string>>(method.Invoke(null, [config, teamIndex, networkNames]));
    }
}
