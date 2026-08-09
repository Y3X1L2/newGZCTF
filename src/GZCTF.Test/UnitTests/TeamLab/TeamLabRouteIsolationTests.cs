using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Validation;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRouteIsolationTests
{
    [Fact]
    public void ForwardPolicies_IncludeRemoteSourcesEnteringTheLocalShard()
    {
        var localNode = Guid.NewGuid();
        var remoteNode = Guid.NewGuid();
        var networks = new[]
        {
            Network(1, 10, remoteNode, "entry", "10.62.0.0/24", "10.62.0.1"),
            Network(2, 20, localNode, "core", "172.23.0.0/24", "172.23.0.1"),
            Network(3, 10, remoteNode, "data", "192.168.0.0/24", "192.168.0.1")
        };
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            TeamLabReachabilityCompiler.Pair("entry", "core"),
            TeamLabReachabilityCompiler.Pair("core", "data"),
            TeamLabReachabilityCompiler.Pair("data", "core")
        };

        var policies = TeamLabRouteApplicationService.BuildForwardPolicies(networks, 20, allowed);

        Assert.Contains(policies, policy => policy.SourceCidr == "10.62.0.0/24" &&
                                             policy.DestinationCidr == "172.23.0.0/24" && policy.Allow);
        Assert.Contains(policies, policy => policy.SourceCidr == "172.23.0.0/24" &&
                                             policy.DestinationCidr == "10.62.0.0/24" && !policy.Allow);
        Assert.Contains(policies, policy => policy.SourceCidr == "192.168.0.0/24" &&
                                             policy.DestinationCidr == "172.23.0.0/24" && policy.Allow);
    }

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
        context.TeamLabFabricLinkLeases.Add(new TeamLabFabricLinkLease
        {
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            ShardId = shard.Id,
            WorkerNodeId = node.Id,
            AllocatedCidr = new IPNetwork(IPAddress.Parse("169.254.0.0"), 30),
            HubAddress = "169.254.0.1",
            NodeAddress = "169.254.0.2"
        });
        await context.SaveChangesAsync();
        TeamLabNodeInfrastructureApplyRequest? applied = null;
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.ApplyInfrastructureAsync(
                node.Id,
                It.IsAny<TeamLabNodeInfrastructureApplyRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, TeamLabNodeInfrastructureApplyRequest, CancellationToken>((_, request, _) => applied = request)
            .ReturnsAsync(TeamLabNodeInfrastructureResult.Applied("sha256:test"));
        var definition = new TeamLabTopologyDefinitionModel(
            "route policy",
            [],
            [],
            [new TeamLabTopologyConnectionModel(
                "entry-core", "entry", "core", "router",
                Direction: TeamLabConnectionDirection.FromTo)]);

        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        await new TeamLabRouteApplicationService(
                context,
                executor.Object,
                new TeamLabEventRecorder(context, writer, new OperationalCorrelation()))
            .ApplyAsync(runtime, TeamLabTopologyV2Compiler.Compile(definition), CancellationToken.None);

        Assert.NotNull(applied);
        Assert.Empty(applied.Fabric.RemoteRoutes);
        Assert.Contains(applied.ForwardPolicies, policy =>
            policy.SourceCidr == "10.10.0.0/24" &&
            policy.DestinationCidr == "192.168.20.0/24" &&
            policy.Allow);
        Assert.Contains(applied.ForwardPolicies, policy =>
            policy.SourceCidr == "192.168.20.0/24" &&
            policy.DestinationCidr == "10.10.0.0/24" &&
            !policy.Allow);
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
