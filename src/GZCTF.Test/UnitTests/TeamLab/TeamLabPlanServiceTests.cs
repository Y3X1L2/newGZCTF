using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Game;
using GZCTF.Services.TeamLab;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabPlanServiceTests
{
    [Fact]
    public void SelectNode_RejectsWhenNoHealthyTeamLabNode()
    {
        var nodes = new[]
        {
            new WorkerNode
            {
                Status = NodeStatus.Online,
                IsLocal = true,
                IsSchedulable = true,
                Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
                TeamLabNetworkEnabled = false
            }
        };

        var result = TeamLabPlanService.SelectNode(nodes);

        Assert.False(result.Success);
        Assert.Contains("TeamLabNetwork", result.Message);
    }

    [Fact]
    public void SelectNode_ReturnsHealthyTeamLabNode()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.10",
            TeamLabAgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 3
        };

        var result = TeamLabPlanService.SelectNode([node]);

        Assert.True(result.Success);
        Assert.Equal(node.Id, result.Node?.Id);
    }

    [Fact]
    public void SelectNode_WhenTargetNodeIsProvided_OnlySelectsThatNode()
    {
        var penetrationNodeId = Guid.NewGuid();
        var otherHealthyNode = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.11",
            TeamLabAgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 3
        };
        var penetrationNode = new WorkerNode
        {
            Id = penetrationNodeId,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.10",
            TeamLabAgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 3
        };

        var result = TeamLabPlanService.SelectNode([otherHealthyNode, penetrationNode], penetrationNodeId);

        Assert.True(result.Success);
        Assert.Equal(penetrationNodeId, result.Node?.Id);
    }

    [Fact]
    public void SelectNode_WhenTargetNodeIsNotHealthy_DoesNotFallbackToOtherNode()
    {
        var penetrationNodeId = Guid.NewGuid();
        var otherHealthyNode = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.250.0.11",
            TeamLabAgentVersion = "1.8.3-test",
            TeamLabProtocolVersion = 3
        };
        var penetrationNode = new WorkerNode
        {
            Id = penetrationNodeId,
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = false
        };

        var result = TeamLabPlanService.SelectNode([otherHealthyNode, penetrationNode], penetrationNodeId);

        Assert.False(result.Success);
        Assert.Null(result.Node);
        Assert.Contains("deployed penetration environment", result.Message);
    }

    [Fact]
    public void SelectNode_RejectsMalformedTeamLabTunnelIp()
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Status = NodeStatus.Online,
            IsLocal = true,
            IsSchedulable = true,
            Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "not-an-ip"
        };

        var result = TeamLabPlanService.SelectNode([node]);

        Assert.False(result.Success);
        Assert.Null(result.Node);
        Assert.Contains("TeamLabNetwork", result.Message);
    }

    [Fact]
    public async Task PlanRuntimeAsync_PersistsShardPlanAndUsesEntryShardAsCompatibilityNode()
    {
        await using var context = CreateContext();
        var nodeA = HealthyNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "node-a", maxContainers: 2);
        var nodeB = HealthyNode("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "node-b", maxContainers: 2);
        context.WorkerNodes.AddRange(nodeA, nodeB);
        context.Games.Add(new Game { Id = 1, Title = "TeamLab", GameType = GameType.Penetration });
        context.Teams.Add(new Team { Id = 2, Name = "team-a" });
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 10,
            Name = "web",
            RegistryUrl = "registry.local/web:latest",
            ImageType = ImageType.Docker,
            OSType = OSType.Linux,
            Status = ImageStatus.Ready
        });
        context.PenetrationConfigs.Add(new PenetrationConfig
        {
            Id = 5,
            GameId = 1,
            Status = PenetrationDeploymentStatus.Published,
            PublishedVersion = 1,
            BaseCidr = "10.60.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28
        });
        context.PenetrationPublishedSnapshots.Add(new PenetrationPublishedSnapshot
        {
            Id = 9,
            GameId = 1,
            PublishedVersion = 1,
            SnapshotHash = "snapshot",
            SnapshotJson = BuildTwoNetworkSnapshot()
        });
        await context.SaveChangesAsync();
        var service = new TeamLabPlanService(context,
            Options.Create(new TeamLabNetworkConfig
            {
                RuntimeNetworkBaseCidr = "10.180.0.0/16",
                TeamSubnetPrefixLength = 24,
                PublicUdpPortStart = 32000,
                PublicUdpPortEnd = 32010,
                WorkerWireGuardPortStart = 42000,
                WorkerWireGuardPortEnd = 42010
            }),
            NullLogger<TeamLabPlanService>.Instance);

        var result = await service.PlanRuntimeAsync(gameId: 1, teamId: 2, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var runtime = await context.TeamLabRuntimes
            .Include(r => r.Shards)
            .Include(r => r.Networks)
            .FirstAsync(r => r.GameId == 1 && r.TeamId == 2);
        Assert.Equal(TeamLabRuntimeStatus.Scheduled, runtime.Status);
        Assert.Equal(nodeA.Id, runtime.WorkerNodeId);
        Assert.Equal(2, runtime.Shards.Count);
        Assert.Contains(runtime.Shards, shard => shard.WorkerNodeId == nodeA.Id);
        Assert.Contains(runtime.Shards, shard => shard.WorkerNodeId == nodeB.Id);
    }

    [Fact]
    public async Task PlanRuntimeAsync_RebuildsScheduledRuntimeWhenShardPlanIsMissing()
    {
        await using var context = CreateContext();
        var node = HealthyNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "node-a", maxContainers: 8);
        context.WorkerNodes.Add(node);
        context.Games.Add(new Game { Id = 1, Title = "TeamLab", GameType = GameType.Penetration });
        context.Teams.Add(new Team { Id = 2, Name = "team-a" });
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 10,
            Name = "web",
            RegistryUrl = "registry.local/web:latest",
            ImageType = ImageType.Docker,
            OSType = OSType.Linux,
            Status = ImageStatus.Ready
        });
        context.PenetrationConfigs.Add(new PenetrationConfig
        {
            Id = 5,
            GameId = 1,
            Status = PenetrationDeploymentStatus.Published,
            PublishedVersion = 1,
            BaseCidr = "10.60.0.0/16",
            TeamSubnetPrefix = 24,
            NetworkSubnetPrefix = 28
        });
        context.PenetrationPublishedSnapshots.Add(new PenetrationPublishedSnapshot
        {
            Id = 9,
            GameId = 1,
            PublishedVersion = 1,
            SnapshotHash = "snapshot",
            SnapshotJson = BuildTwoNetworkSnapshot()
        });
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            Id = 7,
            GameId = 1,
            TeamId = 2,
            PublishedVersion = 1,
            WorkerNodeId = node.Id,
            NetworkPrefix = "10.180.6.0/24",
            Status = TeamLabRuntimeStatus.Scheduled,
            PublicUdpMapping = new TeamLabPublicUdpMapping
            {
                RuntimeId = 7,
                PublicUdpPort = 32000,
                WorkerWireGuardPort = 42000,
                WorkerTunnelIp = "10.250.0.2"
            }
        });
        await context.SaveChangesAsync();
        var service = new TeamLabPlanService(context,
            Options.Create(new TeamLabNetworkConfig
            {
                RuntimeNetworkBaseCidr = "10.180.0.0/16",
                TeamSubnetPrefixLength = 24,
                PublicUdpPortStart = 32000,
                PublicUdpPortEnd = 32010,
                WorkerWireGuardPortStart = 42000,
                WorkerWireGuardPortEnd = 42010
            }),
            NullLogger<TeamLabPlanService>.Instance);

        var result = await service.PlanRuntimeAsync(gameId: 1, teamId: 2, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var runtime = await context.TeamLabRuntimes
            .Include(r => r.Shards)
            .Include(r => r.Networks)
            .Include(r => r.Assets)
            .SingleAsync(r => r.Id == 7);
        Assert.NotEmpty(runtime.Shards);
        Assert.NotEmpty(runtime.Networks);
        Assert.NotEmpty(runtime.Assets);
    }

    [Fact]
    public void AllocatePublicUdpPort_UsesConfiguredRangeAndSkipsUsedPorts()
    {
        var port = TeamLabPlanService.AllocatePublicUdpPort(32000, 32003, new HashSet<int> { 32000, 32001 });

        Assert.Equal(32002, port);
    }

    [Fact]
    public void AllocatePublicUdpPort_ReturnsNullWhenRangeIsExhausted()
    {
        var port = TeamLabPlanService.AllocatePublicUdpPort(32000, 32001, new HashSet<int> { 32000, 32001 });

        Assert.Null(port);
    }

    [Theory]
    [InlineData(PenetrationDeploymentStatus.Published, 3, 3)]
    [InlineData(PenetrationDeploymentStatus.Running, 4, 4)]
    public void ResolvePublishedVersion_AcceptsPublishedOrRunningConfig(PenetrationDeploymentStatus status,
        int publishedVersion, int expected)
    {
        var result = TeamLabPlanService.ResolvePublishedVersion(new PenetrationConfig
        {
            Status = status,
            PublishedVersion = publishedVersion
        });

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(PenetrationDeploymentStatus.Draft, 1)]
    [InlineData(PenetrationDeploymentStatus.Published, 0)]
    [InlineData(PenetrationDeploymentStatus.Failed, 2)]
    public void ResolvePublishedVersion_ReturnsNullWhenNoDeployablePublishedSnapshot(
        PenetrationDeploymentStatus status, int publishedVersion)
    {
        var result = TeamLabPlanService.ResolvePublishedVersion(new PenetrationConfig
        {
            Status = status,
            PublishedVersion = publishedVersion
        });

        Assert.Null(result);
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    static WorkerNode HealthyNode(string id, string name, int maxContainers) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        HostAddress = name,
        AuthToken = "token",
        Status = NodeStatus.Online,
        IsLocal = true,
        IsSchedulable = true,
        Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
        MaxContainers = maxContainers,
        MaxVms = 1,
        CpuLoad = 0.1f,
        MemoryLoad = 0.1f,
        TeamLabNetworkEnabled = true,
        TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
        TeamLabTunnelIp = id.StartsWith('a') ? "10.250.0.2" : "10.250.0.3",
        TeamLabAgentVersion = "1.8.3-test",
        TeamLabProtocolVersion = 3
    };

    static string BuildTwoNetworkSnapshot() => JsonSerializer.Serialize(new PenetrationConfigModel
    {
        GameId = 1,
        BaseCidr = "10.60.0.0/16",
        TeamSubnetPrefix = 24,
        NetworkSubnetPrefix = 28,
        PublishedVersion = 1,
        Status = PenetrationDeploymentStatus.Published,
        Networks =
        [
            new PenetrationNetworkModel { Id = 10, TopologyKey = "entry", Name = "Entry", Slug = "entry", Cidr = "10.10.10.0/24", OrderIndex = 0 },
            new PenetrationNetworkModel { Id = 20, TopologyKey = "data", Name = "Data", Slug = "data", Cidr = "192.168.20.0/24", OrderIndex = 1 }
        ],
        Nodes =
        [
            new PenetrationNodeModel { Id = 101, TopologyKey = "web-a", NetworkId = 10, Name = "Web A", ImageTemplateId = 10, OrderIndex = 0 },
            new PenetrationNodeModel { Id = 102, TopologyKey = "web-b", NetworkId = 10, Name = "Web B", ImageTemplateId = 10, OrderIndex = 1 },
            new PenetrationNodeModel { Id = 201, TopologyKey = "db-a", NetworkId = 20, Name = "DB A", ImageTemplateId = 10, OrderIndex = 2 },
            new PenetrationNodeModel { Id = 202, TopologyKey = "db-b", NetworkId = 20, Name = "DB B", ImageTemplateId = 10, OrderIndex = 3 }
        ]
    });
}
