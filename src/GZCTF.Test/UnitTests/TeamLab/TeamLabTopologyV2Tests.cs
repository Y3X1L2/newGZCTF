using System;
using System.Linq;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabTopologyV2Tests
{
    [Fact]
    public void ReleaseCodec_V2CanonicalizesManagedInfrastructureAndDependencies()
    {
        var source = CreateManagedDefinition();
        var reordered = source with
        {
            Networks = source.Networks.Reverse().ToArray(),
            Infrastructure = source.Infrastructure!.Reverse().ToArray(),
            Assets = source.Assets.Reverse().ToArray(),
            Connections = source.Connections.Reverse().ToArray(),
            Dependencies = source.Dependencies!.Reverse().ToArray()
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
        Assert.Single(execution.Dependencies);
        Assert.True(execution.Observation.FlowMetadataEnabled);
    }

    [Fact]
    public void OpenContract_PreservesV2InfrastructureDependenciesAndObservation()
    {
        var definition = CreateManagedDefinition();
        var request = new OpenCreateTeamLabTopologyModel(
            definition.Name,
            definition.Networks,
            definition.Assets,
            definition.Connections,
            null,
            definition.Infrastructure,
            definition.Dependencies,
            definition.Observation,
            SchemaVersion: 2);

        var mapped = request.ToInternal();

        Assert.Equal(2, mapped.SchemaVersion);
        Assert.Equal(definition.Infrastructure, mapped.Infrastructure);
        Assert.Equal(definition.Dependencies, mapped.Dependencies);
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

    [Fact]
    public void Validate_AcceptsManagedRouterAndRejectsDependencyCycle()
    {
        var source = CreateManagedDefinition();
        var valid = new TeamLabTopologyValidator().Validate(source, 2);
        var cyclic = source with
        {
            Dependencies =
            [
                new TeamLabTopologyDependencyModel(
                    "core-api", "entry-web", TeamLabDependencyCondition.ServiceReady),
                new TeamLabTopologyDependencyModel(
                    "entry-web", "core-api", TeamLabDependencyCondition.ServiceReady)
            ]
        };

        var invalid = new TeamLabTopologyValidator().Validate(cyclic, 2);

        Assert.True(valid.Valid, string.Join("; ", valid.Issues.Select(item => item.Message)));
        Assert.Contains(invalid.Issues, item => item.Code == "dependency_cycle");
    }

    [Fact]
    public void Validate_ReservesLastUsableAddressForWireGuardServer()
    {
        var source = CreateManagedDefinition();
        var invalid = source with
        {
            Assets = source.Assets.Select(item => item.Key == "entry-web"
                ? item with
                {
                    Interfaces =
                    [
                        new TeamLabTopologyInterfaceModel("eth0", "entry", 254, true)
                    ]
                }
                : item).ToArray()
        };

        var result = new TeamLabTopologyValidator().Validate(invalid, 2);

        Assert.Contains(result.Issues, item => item.Code == "interface_host_offset_reserved");
    }

    [Fact]
    public void BuildGroups_DoesNotCollapseNetworksConnectedByManagedRouter()
    {
        var execution = TeamLabTopologyV2Compiler.Compile(CreateManagedDefinition());

        var groups = TeamLabAssetPlanner.BuildGroups(execution);

        Assert.Equal(3, groups.Count);
        Assert.All(groups, group => Assert.Single(group.NetworkKeys));
    }

    [Fact]
    public void BuildGroups_KeepsImageBackedMultiNicApplianceOnOneNode()
    {
        var definition = CreateLegacyDefinition();
        var execution = TeamLabTopologyV1Normalizer.Normalize(definition);

        var groups = TeamLabAssetPlanner.BuildGroups(execution);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].NetworkKeys.Count);
    }

    [Fact]
    public void Planner_CoLocatesHighestCostManagedRouterNetworksWhenCapacityAllows()
    {
        var execution = TeamLabTopologyV2Compiler.Compile(CreateManagedDefinition());
        var nodes = new[]
        {
            new TeamLabPlanningNodeSnapshot(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "node-a", true, false, 2, 0, .1f, .1f),
            new TeamLabPlanningNodeSnapshot(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "node-b", true, false, 1, 0, .1f, .1f)
        };

        var plan = TeamLabAssetPlanner.Build(Guid.NewGuid(), Guid.NewGuid(), execution, nodes);

        Assert.Equal(2, plan.Shards.Count);
        Assert.Contains(plan.Shards, shard =>
            shard.NetworkKeys.Contains("entry") && shard.NetworkKeys.Contains("core"));
        Assert.Equal(4, plan.ManagedInfrastructureCount);
        Assert.True(plan.ObservationPointEstimate >= 7);
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
        Dependencies:
        [
            new TeamLabTopologyDependencyModel(
                "core-api", "entry-web", TeamLabDependencyCondition.ServiceReady)
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
