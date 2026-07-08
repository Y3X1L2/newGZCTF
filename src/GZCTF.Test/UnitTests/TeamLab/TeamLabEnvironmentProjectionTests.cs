using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabEnvironmentProjectionTests
{
    [Fact]
    public async Task GetTeamEnvironments_IncludesTeamLabShardNetworkAssetAndCaptureFacts()
    {
        await using var context = CreateContext();
        var nodeA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var nodeB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        await SeedRuntimeAsync(context, nodeA, nodeB);
        var service = CreateService(context);

        var rows = await service.GetTeamEnvironments(1, CancellationToken.None);

        var env = Assert.Single(rows);
        Assert.Equal(2, env.TeamLabShards.Count);
        Assert.Contains(env.TeamLabShards, shard =>
            shard.WorkerNodeId == nodeA &&
            shard.WorkerNodeName == "node-a" &&
            shard.NetworkKeys.SequenceEqual(["entry"]) &&
            shard.AssetKeys.SequenceEqual(["portal"]));
        Assert.Contains(env.TeamLabNetworks, network =>
            network.TopologyKey == "data" &&
            network.WorkerNodeId == nodeB &&
            network.ShardId == 22 &&
            network.Cidr == "192.168.80.0/24");
        Assert.Contains(env.TeamLabAssets, asset =>
            asset.TopologyKey == "db" &&
            asset.WorkerNodeId == nodeB &&
            asset.Kind == TeamLabResourceKind.Docker &&
            asset.IpAddress == "192.168.80.10");
        var capture = Assert.Single(env.TeamLabCaptureJobs);
        Assert.Equal(TeamLabTrafficCaptureStatus.Running, capture.Status);
        Assert.Equal("network:entry", capture.Scope);
        Assert.Equal(4096, capture.CapturedBytes);
        var flow = Assert.Single(env.TeamLabTrafficFlows);
        Assert.Equal("Entry", flow.NetworkName);
        Assert.Equal("10.180.33.10", flow.SourceIp);
        Assert.Equal(43122, flow.SourcePort);
        Assert.Equal("192.168.80.10", flow.DestinationIp);
        Assert.Equal(80, flow.DestinationPort);
        Assert.Equal("TCP", flow.Protocol);
        Assert.Equal(1460, flow.Bytes);
    }

    static PenetrationService CreateService(AppDbContext context) =>
        new(context, null!, null!, null!, null!, null!, null!, null!,
            NullLogger<PenetrationService>.Instance);

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    static async Task SeedRuntimeAsync(AppDbContext context, Guid nodeA, Guid nodeB)
    {
        var game = new Game { Id = 1, Title = "TeamLab", GameType = GameType.Penetration };
        var team = new Team { Id = 2, Name = "blue" };
        var workerA = new WorkerNode
        {
            Id = nodeA,
            Name = "node-a",
            HostAddress = "10.24.0.30",
            AuthToken = "token-a",
            Status = NodeStatus.Online,
            TeamLabNetworkEnabled = true
        };
        var workerB = new WorkerNode
        {
            Id = nodeB,
            Name = "node-b",
            HostAddress = "10.24.0.31",
            AuthToken = "token-b",
            Status = NodeStatus.Online,
            TeamLabNetworkEnabled = true
        };
        var environment = new PenetrationTeamEnvironment
        {
            Id = 9,
            Game = game,
            GameId = game.Id,
            Team = team,
            TeamId = team.Id,
            Node = workerA,
            NodeId = workerA.Id,
            NetworkPrefix = "10.180.0.0/16",
            TeamIndex = 1,
            PublishedVersion = 3,
            Status = PenetrationRuntimeStatus.Running
        };
        var runtime = new TeamLabRuntime
        {
            Id = 10,
            Game = game,
            GameId = game.Id,
            Team = team,
            TeamId = team.Id,
            WorkerNode = workerA,
            WorkerNodeId = workerA.Id,
            NetworkPrefix = "10.180.0.0/16",
            PublishedVersion = 3,
            Status = TeamLabRuntimeStatus.Running
        };
        var shardA = new TeamLabRuntimeShard
        {
            Id = 21,
            Runtime = runtime,
            RuntimeId = runtime.Id,
            WorkerNode = workerA,
            WorkerNodeId = workerA.Id,
            Status = TeamLabRuntimeStatus.Running,
            RouteVersion = 3
        };
        var shardB = new TeamLabRuntimeShard
        {
            Id = 22,
            Runtime = runtime,
            RuntimeId = runtime.Id,
            WorkerNode = workerB,
            WorkerNodeId = workerB.Id,
            Status = TeamLabRuntimeStatus.Running,
            RouteVersion = 3
        };
        runtime.Shards.AddRange([shardA, shardB]);
        runtime.Networks.AddRange([
            new TeamLabRuntimeNetwork
            {
                Runtime = runtime,
                RuntimeId = runtime.Id,
                Shard = shardA,
                ShardId = shardA.Id,
                WorkerNode = workerA,
                WorkerNodeId = workerA.Id,
                TopologyKey = "entry",
                Name = "Entry",
                Cidr = "10.180.33.0/24",
                GatewayIp = "10.180.33.1",
                BridgeName = "tl10-entry"
            },
            new TeamLabRuntimeNetwork
            {
                Runtime = runtime,
                RuntimeId = runtime.Id,
                Shard = shardB,
                ShardId = shardB.Id,
                WorkerNode = workerB,
                WorkerNodeId = workerB.Id,
                TopologyKey = "data",
                Name = "Data",
                Cidr = "192.168.80.0/24",
                GatewayIp = "192.168.80.1",
                BridgeName = "tl10-data"
            }
        ]);
        runtime.Assets.AddRange([
            new TeamLabRuntimeAsset
            {
                Runtime = runtime,
                RuntimeId = runtime.Id,
                Shard = shardA,
                ShardId = shardA.Id,
                WorkerNode = workerA,
                WorkerNodeId = workerA.Id,
                Kind = TeamLabResourceKind.Docker,
                TopologyKey = "portal",
                Name = "Portal",
                RuntimeResourceId = "container-portal",
                NetworkKey = "entry",
                IpAddress = "10.180.33.10",
                Status = TeamLabRuntimeStatus.Running
            },
            new TeamLabRuntimeAsset
            {
                Runtime = runtime,
                RuntimeId = runtime.Id,
                Shard = shardB,
                ShardId = shardB.Id,
                WorkerNode = workerB,
                WorkerNodeId = workerB.Id,
                Kind = TeamLabResourceKind.Docker,
                TopologyKey = "db",
                Name = "Database",
                RuntimeResourceId = "container-db",
                NetworkKey = "data",
                IpAddress = "192.168.80.10",
                Status = TeamLabRuntimeStatus.Running
            }
        ]);
        runtime.TrafficCaptureJobs.Add(new TeamLabTrafficCaptureJob
        {
            Id = 31,
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Shard = shardA,
            ShardId = shardA.Id,
            NetworkId = runtime.Networks[0].Id,
            WorkerNode = workerA,
            WorkerNodeId = workerA.Id,
            Status = TeamLabTrafficCaptureStatus.Running,
            Scope = "network:entry",
            MaxSeconds = 120,
            MaxBytes = 16 * 1024 * 1024,
            CapturedBytes = 4096,
            FilePath = "/run/gzctf-teamlab/capture-10-31/capture.pcap"
        });
        runtime.TrafficFlows.Add(new TeamLabTrafficFlow
        {
            Runtime = runtime,
            RuntimeId = runtime.Id,
            Shard = shardA,
            ShardId = shardA.Id,
            Network = runtime.Networks[0],
            WorkerNode = workerA,
            WorkerNodeId = workerA.Id,
            SourceIp = "10.180.33.10",
            SourcePort = 43122,
            DestinationIp = "192.168.80.10",
            DestinationPort = 80,
            Protocol = "TCP",
            Bytes = 1460,
            CapturedAt = DateTimeOffset.UtcNow
        });

        context.Games.Add(game);
        context.Teams.Add(team);
        context.WorkerNodes.AddRange(workerA, workerB);
        context.PenetrationTeamEnvironments.Add(environment);
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
    }
}
