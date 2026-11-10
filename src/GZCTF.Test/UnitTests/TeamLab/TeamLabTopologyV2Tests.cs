using System;
using System.Linq;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabTopologyV2Tests
{
    [Fact]
    public void ReleaseCodec_V2CanonicalizesManagedInfrastructure()
    {
        var source = CreateManagedDefinition();
        var reordered = source with
        {
            Networks = source.Networks.Reverse().ToArray(),
            Infrastructure = source.Infrastructure!.Reverse().ToArray(),
            Assets = source.Assets.Reverse().ToArray(),
            Connections = source.Connections.Reverse().ToArray()
        };

        var first = TeamLabReleaseCodec.Encode(2, source);
        var second = TeamLabReleaseCodec.Encode(2, reordered);
        var execution = TeamLabReleaseCodec.DecodeExecution(2, first);

        Assert.Equal(first, second);
        Assert.Equal(3, execution.Infrastructure.Count(item =>
            item.Kind == TeamLabInfrastructureKind.ManagedSwitch));
        Assert.Single(execution.Infrastructure, item =>
            item.Kind == TeamLabInfrastructureKind.ManagedRouter);
        Assert.Equal(
            TeamLabConnectionDirection.FromTo,
            execution.Connections.Single(item => item.Key == "entry-core").Direction);
        Assert.True(execution.Observation.FlowMetadataEnabled);
    }

    [Fact]
    public void OpenContract_PreservesV2InfrastructureAndObservation()
    {
        var definition = CreateManagedDefinition();
        var request = new OpenCreateTeamLabTopologyModel(
            definition.Name,
            definition.Networks,
            definition.Assets,
            definition.Connections,
            null,
            definition.Infrastructure,
            definition.Observation,
            SchemaVersion: 2);

        var mapped = request.ToInternal();

        Assert.Equal(2, mapped.SchemaVersion);
        Assert.Equal(definition.Infrastructure, mapped.Infrastructure);
        Assert.Equal(definition.Observation, mapped.Observation);
    }

    [Fact]
    public void ReleaseCodec_V1KeepsLegacyCanonicalShapeAndNormalizesExecution()
    {
        var source = CreateLegacyDefinition();

        var canonical = TeamLabReleaseCodec.Encode(1, source);
        var execution = TeamLabReleaseCodec.DecodeExecution(1, canonical);

        Assert.DoesNotContain("infrastructure", canonical, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stateless", canonical, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, execution.Infrastructure.Count);
        Assert.All(execution.Infrastructure, item => Assert.True(item.Implicit));
        Assert.Equal(TeamLabConnectionDirection.Bidirectional, execution.Connections[0].Direction);
    }

    private static TeamLabTopologyDefinitionModel CreateManagedDefinition() => new(
        "managed-fabric",
        [
            Network("entry", "10.32.0.0/16", true),
            Network("core", "172.20.0.0/16", false),
            Network("data", "192.168.0.0/16", false)
        ],
        [
            Asset("entry-web", "entry", 1, 10),
            Asset("core-api", "core", 2, 10),
            Asset("data-db", "data", 3, 10)
        ],
        [
            new TeamLabTopologyConnectionModel(
                "entry-core", "entry", "core", ViaNodeKey: "edge-router",
                Direction: TeamLabConnectionDirection.FromTo),
            new TeamLabTopologyConnectionModel(
                "core-data", "core", "data", ViaNodeKey: "edge-router",
                Direction: TeamLabConnectionDirection.Bidirectional)
        ],
        Infrastructure:
        [
            new TeamLabTopologyInfrastructureModel(
                "edge-router",
                "Edge Router",
                TeamLabInfrastructureKind.ManagedRouter,
                [
                    new TeamLabTopologyInterfaceModel("entry-if", "entry", 1, true),
                    new TeamLabTopologyInterfaceModel("core-if", "core", 1, false),
                    new TeamLabTopologyInterfaceModel("data-if", "data", 1, false)
                ])
        ],
        Observation: new TeamLabObservationPolicyModel());

    private static TeamLabTopologyDefinitionModel CreateLegacyDefinition() => new(
        "legacy-router",
        [
            Network("entry", "10.40.0.0/16", true),
            Network("core", "192.168.0.0/16", false)
        ],
        [
            new TeamLabTopologyAssetModel(
                "router",
                "Router",
                TeamLabAssetKind.Docker,
                1,
                new TeamLabAssetResourceModel(10, 256, 512),
                [
                    new TeamLabTopologyInterfaceModel("eth0", "entry", 10, true),
                    new TeamLabTopologyInterfaceModel("eth1", "core", 10, false)
                ],
                ExposePort: null)
        ],
        [new TeamLabTopologyConnectionModel("entry-core", "entry", "core", "router")]);

    private static TeamLabTopologyNetworkModel Network(string key, string pool, bool entry) =>
        new(key, key, new TeamLabAddressPoolModel(pool, 24), entry);

    private static TeamLabTopologyAssetModel Asset(string key, string network, int templateId, int hostOffset) =>
        new(
            key,
            key,
            TeamLabAssetKind.Docker,
            templateId,
            new TeamLabAssetResourceModel(10, 256, 512),
            [new TeamLabTopologyInterfaceModel("eth0", network, hostOffset, true)],
            ExposePort: null);
}
