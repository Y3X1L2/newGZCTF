using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabFoundationTopologyTests
{
    [Fact]
    public void Validate_AcceptsMixedRfc1918Topology()
    {
        var result = new TeamLabTopologyValidator().Validate(CreateDefinition());

        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(item => item.Message)));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_RejectsOverlappingPoolsAndInvalidRouter()
    {
        var source = CreateDefinition();
        var invalid = source with
        {
            Networks =
            [
                source.Networks[0],
                source.Networks[1] with { AddressPool = new TeamLabAddressPoolModel("10.40.0.0/17", 24) }
            ],
            Assets = source.Assets.Select(item => item with { RoutingEnabled = false }).ToArray()
        };

        var result = new TeamLabTopologyValidator().Validate(invalid);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, item => item.Code == "address_pool_overlap");
        Assert.Contains(result.Issues, item => item.Code == "connection_router_invalid");
    }

    [Fact]
    public void ReleaseCodec_IsDeterministicAcrossInputOrder()
    {
        var source = CreateDefinition();
        var reordered = source with
        {
            Networks = source.Networks.Reverse().ToArray(),
            Assets = source.Assets.Reverse().ToArray(),
            Connections = source.Connections.Reverse().ToArray()
        };

        var first = TeamLabReleaseCodec.Encode(source);
        var second = TeamLabReleaseCodec.Encode(reordered);

        Assert.Equal(first, second);
        Assert.Equal(
            TeamLabReleaseCodec.ComputeContentHash(1, first),
            TeamLabReleaseCodec.ComputeContentHash(1, second));
        Assert.DoesNotContain("gameId", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("teamId", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Planner_SplitsIndependentNetworkGroupsWithoutExposingNodes()
    {
        var source = CreateDefinition() with
        {
            Assets =
            [
                DockerAsset("entry-web", "entry", 10),
                DockerAsset("entry-api", "entry", 11),
                DockerAsset("core-db", "core", 12)
            ],
            Connections = []
        };
        var nodes = new[]
        {
            new TeamLabPlanningNodeSnapshot(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "node-a", true, false, 2, 0, .1f, .1f),
            new TeamLabPlanningNodeSnapshot(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "node-b", true, false, 1, 0, .1f, .1f)
        };

        var plan = TeamLabAssetPlanner.Build(Guid.NewGuid(), Guid.NewGuid(), source, nodes);
        var json = JsonSerializer.Serialize(plan);

        Assert.Equal(2, plan.Shards.Count);
        Assert.Contains(plan.Shards, shard => shard.NetworkKeys.SequenceEqual(["entry"]));
        Assert.Contains(plan.Shards, shard => shard.NetworkKeys.SequenceEqual(["core"]));
        Assert.DoesNotContain("node-a", json, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaaaaaa-aaaa", json, StringComparison.Ordinal);
    }

    private static TeamLabTopologyDefinitionModel CreateDefinition() => new(
        "enterprise-lab",
        [
            new TeamLabTopologyNetworkModel("entry", "Entry", new TeamLabAddressPoolModel("10.40.0.0/16", 24), true),
            new TeamLabTopologyNetworkModel("core", "Core", new TeamLabAddressPoolModel("192.168.0.0/16", 24), false)
        ],
        [
            new TeamLabTopologyAssetModel(
                "jump-host", "Jump Host", TeamLabAssetKind.Docker, 1,
                new TeamLabAssetResourceModel(10, 512, 2048),
                [
                    new TeamLabTopologyInterfaceModel("eth0", "entry", 10, true),
                    new TeamLabTopologyInterfaceModel("eth1", "core", 10, false)
                ],
                true,
                22,
                new Dictionary<string, string> { ["LAB_MODE"] = "competition" },
                HealthCheck: new TeamLabHealthCheckModel(TeamLabHealthCheckKind.Tcp, 22))
        ],
        [new TeamLabTopologyConnectionModel("entry-to-core", "entry", "core", "jump-host")]);

    private static TeamLabTopologyAssetModel DockerAsset(string key, string networkKey, int templateId) => new(
        key,
        key,
        TeamLabAssetKind.Docker,
        templateId,
        new TeamLabAssetResourceModel(10, 256, 512),
        [new TeamLabTopologyInterfaceModel("eth0", networkKey, 10 + templateId, true)],
        false);
}
