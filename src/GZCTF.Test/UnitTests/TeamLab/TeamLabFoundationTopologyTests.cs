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
