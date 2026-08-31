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
    public void Validate_RejectsOverlappingPools()
    {
        var source = CreateDefinition();
        var invalid = source with
        {
            Networks =
            [
                source.Networks[0],
                source.Networks[1] with { AddressPool = new TeamLabAddressPoolModel("10.40.0.0/17", 24) }
            ],
            Assets = source.Assets
        };

        var result = new TeamLabTopologyValidator().Validate(invalid);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, item => item.Code == "address_pool_overlap");
    }

    [Fact]
    public void Validate_RejectsAddressPoolOverlappingPlatformReservedRange()
    {
        // The pool's runtime CIDRs end up in the WorkerNode host routing table, so overlapping
        // docker0 would shadow the node's own routes and break unrelated games on that node.
        var source = CreateDefinition();
        var conflicting = source with
        {
            Networks =
            [
                source.Networks[0] with { AddressPool = new TeamLabAddressPoolModel("172.17.0.0/16", 24) },
                source.Networks[1]
            ]
        };

        var result = new TeamLabTopologyValidator().Validate(conflicting);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, item => item.Code == "address_pool_reserved");
    }

    [Fact]
    public void Validate_AcceptsPrivatePoolsOutsideReservedRanges()
    {
        var result = new TeamLabTopologyValidator().Validate(CreateDefinition());

        Assert.DoesNotContain(result.Issues, item => item.Code == "address_pool_reserved");
    }

    [Fact]
    public void Validate_RejectsAddressPoolOutsideTheConfiguredRuntimeRange()
    {
        // The platform allocates runtime networks from this range; a pool outside it produces host
        // routes on the WorkerNode that the platform does not own.
        var policy = GZCTF.Modules.TeamLab.Application.Validation.TeamLabAddressPolicy.ForPlatform(
            null, "100.64.0.0/16", "10.180.0.0/16");

        var result = new TeamLabTopologyValidator(policy).Validate(CreateDefinition());

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, item => item.Code == "address_pool_out_of_platform_range");
    }

    [Fact]
    public void Validate_AcceptsAddressPoolsInsideTheConfiguredRuntimeRange()
    {
        var policy = GZCTF.Modules.TeamLab.Application.Validation.TeamLabAddressPolicy.ForPlatform(
            null, "100.64.0.0/16", "10.180.0.0/16");
        var source = CreateDefinition();
        var compliant = source with
        {
            Networks =
            [
                source.Networks[0] with { AddressPool = new TeamLabAddressPoolModel("10.180.0.0/18", 24) },
                source.Networks[1] with { AddressPool = new TeamLabAddressPoolModel("10.180.64.0/18", 24) }
            ]
        };

        var result = new TeamLabTopologyValidator(policy).Validate(compliant);

        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(item => item.Message)));
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
    public void ReleaseSnapshot_PreservesEditorWithoutChangingExecutionDigest()
    {
        var definition = CreateDefinition();
        var canonical = TeamLabReleaseCodec.Encode(2, definition);
        var expectedHash = TeamLabReleaseCodec.ComputeContentHash(2, canonical);
        var release = new TeamLabTopologyRelease
        {
            Id = Guid.NewGuid(),
            SchemaVersion = 2,
            CanonicalJson = canonical,
            ContentHash = expectedHash,
            EditorMetadataJson = """
                {"networks":{"entry":{"x":120,"y":80,"width":640,"height":420,"collapsed":false}},"assets":{},"infrastructure":{}}
                """
        };

        var model = TeamLabReleaseService.ToModel(release, Guid.NewGuid());

        Assert.Equal(expectedHash, model.ContentHash);
        Assert.NotNull(model.Editor);
        Assert.Equal(120, model.Editor!.Networks["entry"].X);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ReleaseCodec_PreservesPlatformLockedImageDigest(int schemaVersion)
    {
        var expectedDigest = $"sha256:{new string('a', 64)}";
        var source = CreateDefinition();
        var digests = source.Assets.ToDictionary(
            asset => asset.Key,
            _ => expectedDigest,
            StringComparer.Ordinal);

        var canonical = TeamLabReleaseCodec.Encode(schemaVersion, source, digests);
        var decoded = TeamLabReleaseCodec.DecodeExecution(schemaVersion, canonical);

        Assert.All(decoded.Assets, asset => Assert.Equal(expectedDigest, asset.ImageDigest));
    }

    [Fact]
    public void PublicTopologyDefinition_DoesNotAcceptImageDigest()
    {
        Assert.Null(typeof(TeamLabTopologyAssetModel).GetProperty("ImageDigest"));
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

        var plan = TeamLabAssetPlanner.Build(
            Guid.NewGuid(), Guid.NewGuid(), TeamLabTopologyV2Compiler.Compile(source), nodes);
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
                [new TeamLabTopologyInterfaceModel("eth0", "entry", 10, true)],
                ExposePort: 22,
                HealthCheck: new TeamLabHealthCheckModel(TeamLabHealthCheckKind.Tcp, 22))
        ],
        [new TeamLabTopologyConnectionModel("entry-to-core", "entry", "core", ViaNodeKey: "edge-router")],
        Infrastructure:
        [
            new TeamLabTopologyInfrastructureModel(
                "edge-router", "Edge Router", TeamLabInfrastructureKind.ManagedRouter,
                [
                    new TeamLabTopologyInterfaceModel("entry", "entry", 1, true),
                    new TeamLabTopologyInterfaceModel("core", "core", 1, false)
                ])
        ]);

    private static TeamLabTopologyAssetModel DockerAsset(string key, string networkKey, int templateId) => new(
        key,
        key,
        TeamLabAssetKind.Docker,
        templateId,
        new TeamLabAssetResourceModel(10, 256, 512),
        [new TeamLabTopologyInterfaceModel("eth0", networkKey, 10 + templateId, true)],
        ExposePort: null);
}
