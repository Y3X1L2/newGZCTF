using System.Collections.Generic;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabPublishedTopologyServiceTests
{
    [Fact]
    public void ParsePublishedSnapshot_BuildsTransientTopologyWithImages()
    {
        var snapshot = JsonSerializer.Serialize(new PenetrationConfigModel
        {
            GameId = 5,
            BaseCidr = "10.90.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28,
            PublishedVersion = 3,
            Status = PenetrationDeploymentStatus.Published,
            Networks =
            [
                new PenetrationNetworkModel { Id = 10, TopologyKey = "dmz", Name = "DMZ", Slug = "dmz", IsEntry = true, ZoneType = PenetrationZoneType.Dmz }
            ],
            Nodes =
            [
                new PenetrationNodeModel
                {
                    Id = 101,
                    TopologyKey = "portal",
                    NetworkId = 10,
                    Name = "Portal",
                    ImageTemplateId = 1,
                    IsEntry = true,
                    PublishPort = true,
                    Interfaces =
                    [
                        new PenetrationInterfaceModel { Id = 1001, TopologyKey = "portal-eth0", NodeId = 101, NetworkId = 10, Name = "eth0", IsPrimary = true }
                    ],
                    ScoreItems =
                    [
                        new PenetrationScoreItemModel
                        {
                            Id = 2001,
                            TopologyKey = "portal-flag",
                            Title = "Portal Flag",
                            Description = "Find the first flag.",
                            Category = "Web",
                            Score = 100,
                            IsDynamic = true,
                            FlagTemplate = "flag{[TOKEN]}",
                            IsVisible = true,
                            IsCheckpoint = true,
                            PrerequisiteItemIds = [2000],
                            OrderIndex = 1
                        }
                    ]
                }
            ],
            Edges =
            [
                new PenetrationEdgeModel
                {
                    Id = 3001,
                    TopologyKey = "portal-to-api",
                    SourceNodeId = 101,
                    TargetNodeId = 101,
                    SourceKind = PenetrationPolicyScope.Node,
                    SourceId = 101,
                    TargetKind = PenetrationPolicyScope.Node,
                    TargetId = 101,
                    Protocol = PenetrationProtocol.Any,
                    PortRange = "any",
                    PolicyAction = PenetrationPolicyAction.Allow,
                    IsRouteHint = true,
                    EnforcementMode = PenetrationEnforcementMode.Both,
                    Priority = 20,
                    Label = "内网路由关系",
                    Description = "Published route relationship"
                }
            ]
        });

        var result = TeamLabPublishedTopologyService.ParsePublishedSnapshot(gameId: 5, publishedVersion: 3, snapshot,
            new Dictionary<int, ImageTemplate>
            {
                [1] = new() { Id = 1, Name = "portal", RegistryUrl = "registry.local/portal:latest", ImageType = ImageType.Docker, Status = ImageStatus.Ready }
            });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Config);
        Assert.Single(result.Config.Networks);
        var node = Assert.Single(result.Config.Nodes);
        Assert.Equal("Portal", node.Name);
        Assert.NotNull(node.ImageTemplate);
        Assert.Equal("registry.local/portal:latest", node.ImageTemplate.RegistryUrl);
        Assert.False(result.Config.Networks[0].IsEntry);
        Assert.False(node.IsEntry);
        Assert.False(node.PublishPort);
        Assert.Same(node.Network, Assert.Single(node.Interfaces).Network);
        var score = Assert.Single(node.ScoreItems);
        Assert.Equal("portal-flag", score.TopologyKey);
        Assert.Equal("Portal Flag", score.Title);
        Assert.Equal("Web", score.Category);
        Assert.Equal("flag{[TOKEN]}", score.FlagTemplate);
        Assert.True(score.IsCheckpoint);
        Assert.Equal("[2000]", score.PrerequisiteItemIds);
        var edge = Assert.Single(result.Config.Edges);
        Assert.Equal("portal-to-api", edge.TopologyKey);
        Assert.Equal(PenetrationEnforcementMode.Both, edge.EnforcementMode);
        Assert.Equal(PenetrationPolicyAction.Allow, edge.PolicyAction);
        Assert.Equal(20, edge.Priority);
        Assert.Equal("Published route relationship", edge.Description);
    }

    [Fact]
    public void ParsePublishedSnapshot_RejectsMissingTemplate()
    {
        var snapshot = JsonSerializer.Serialize(new PenetrationConfigModel
        {
            GameId = 5,
            PublishedVersion = 3,
            Networks =
            [
                new PenetrationNetworkModel { Id = 10, TopologyKey = "dmz", Name = "DMZ", Slug = "dmz", IsEntry = true }
            ],
            Nodes =
            [
                new PenetrationNodeModel { Id = 101, TopologyKey = "portal", NetworkId = 10, Name = "Portal", ImageTemplateId = 9 }
            ]
        });

        var result = TeamLabPublishedTopologyService.ParsePublishedSnapshot(gameId: 5, publishedVersion: 3, snapshot,
            new Dictionary<int, ImageTemplate>());

        Assert.False(result.Success);
        Assert.Contains("Portal", result.Message);
        Assert.Contains("template", result.Message);
    }

    [Fact]
    public void ParsePublishedSnapshot_MigratesDeprecatedPublicEntryTopologyToVpnInternalAssets()
    {
        var snapshot = JsonSerializer.Serialize(new PenetrationConfigModel
        {
            GameId = 5,
            PublishedVersion = 3,
            Networks =
            [
                new PenetrationNetworkModel
                {
                    Id = 10,
                    TopologyKey = "public-edge",
                    Name = "Public / Edge",
                    Slug = "public-edge",
                    IsEntry = true,
                    ZoneType = PenetrationZoneType.Public
                }
            ],
            Nodes =
            [
                new PenetrationNodeModel
                {
                    Id = 101,
                    TopologyKey = "edge-gateway",
                    NetworkId = 10,
                    Name = "Edge Gateway",
                    NodeType = PenetrationNodeType.Entry,
                    ImageTemplateId = 1,
                    IsEntry = true,
                    PublishPort = true
                }
            ]
        });

        var result = TeamLabPublishedTopologyService.ParsePublishedSnapshot(gameId: 5, publishedVersion: 3, snapshot,
            new Dictionary<int, ImageTemplate>
            {
                [1] = new()
                {
                    Id = 1,
                    Name = "portal",
                    RegistryUrl = "registry.local/portal:latest",
                    ImageType = ImageType.Docker,
                    Status = ImageStatus.Ready
                }
            });

        Assert.True(result.Success, result.Message);
        var network = Assert.Single(result.Config!.Networks);
        var node = Assert.Single(result.Config.Nodes);
        Assert.False(network.IsEntry);
        Assert.Equal(PenetrationZoneType.Dmz, network.ZoneType);
        Assert.False(node.IsEntry);
        Assert.False(node.PublishPort);
        Assert.Equal(PenetrationNodeType.Web, node.NodeType);
    }
}
