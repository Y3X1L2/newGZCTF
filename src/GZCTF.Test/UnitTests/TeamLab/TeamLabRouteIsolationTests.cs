using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRouteIsolationTests
{
    [Fact]
    public async Task ApplyAsync_AppliesConnectionPolicyToSingleShard()
    {
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "worker-a",
            TeamLabFabricIp = "10.250.0.10"
        };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();

        var shard = new TeamLabRuntimeShard
        {
            Id = 17,
            WorkerNodeId = node.Id,
            Generation = 1
        };
        var runtime = new TeamLabRuntime
        {
            Id = 42,
            Generation = 1,
            Shards = [shard],
            Networks =
            [
                Network(1, shard.Id, node.Id, "entry", "10.10.0.0/24", "10.10.0.1"),
                Network(2, shard.Id, node.Id, "core", "192.168.20.0/24", "192.168.20.1"),
                Network(3, shard.Id, node.Id, "data", "172.20.30.0/24", "172.20.30.1")
            ]
        };
        TeamLabNodeRouteApplyRequest? applied = null;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.ApplyRoutesAsync(
                node.Id,
                It.IsAny<TeamLabNodeRouteApplyRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, TeamLabNodeRouteApplyRequest, CancellationToken>((_, request, _) => applied = request)
            .ReturnsAsync(TeamLabNodeResult.Ok());
        var definition = new TeamLabTopologyDefinitionModel(
            "route policy",
            [],
            [],
            [new TeamLabTopologyConnectionModel("entry-core", "entry", "core", "router")]);

        await new TeamLabRouteApplicationService(context, executor.Object)
            .ApplyAsync(runtime, definition, CancellationToken.None);

        Assert.NotNull(applied);
        Assert.Empty(applied.RemoteRoutes);
        Assert.Contains(applied.ForwardPolicies, policy =>
            policy.SourceCidr == "10.10.0.0/24" &&
            policy.DestinationCidr == "192.168.20.0/24" &&
            policy.Allow);
        Assert.Contains(applied.ForwardPolicies, policy =>
            policy.SourceCidr == "10.10.0.0/24" &&
            policy.DestinationCidr == "172.20.30.0/24" &&
            !policy.Allow);
        Assert.Equal(1, shard.RouteVersion);
    }

    private static TeamLabRuntimeNetwork Network(
        int id,
        int shardId,
        Guid workerNodeId,
        string key,
        string cidr,
        string gateway) =>
        new()
        {
            Id = id,
            Generation = 1,
            ShardId = shardId,
            WorkerNodeId = workerNodeId,
            TopologyKey = key,
            Name = key,
            Cidr = cidr,
            GatewayIp = gateway,
            BridgeName = $"br-{key}"
        };
}
