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

    [Fact]
    public void Validate_AcceptsManagedRouterTopology()
    {
        var source = CreateManagedDefinition();
        var result = new TeamLabTopologyValidator().Validate(source, 2);

        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(item => item.Message)));
    }

    [Fact]
    public void DnsAssetReference_SurvivesReleaseAndCannotPointToRemovedAsset()
    {
        var definition = CreateManagedDefinition();
        definition = definition with
        {
            Networks = definition.Networks.Select(network => network.Key == "core"
                ? network with { DnsServerAssetKey = "core-api" }
                : network).ToArray()
        };

        var released = TeamLabReleaseCodec.DecodeExecution(2, TeamLabReleaseCodec.Encode(2, definition));
        Assert.Equal("core-api", released.Networks.Single(network => network.Key == "core").DnsServerAssetKey);
        Assert.True(new TeamLabTopologyValidator().Validate(definition, 2).Valid);

        var removed = definition with { Assets = definition.Assets.Where(asset => asset.Key != "core-api").ToArray() };
        Assert.Contains(new TeamLabTopologyValidator().Validate(removed, 2).Issues,
            issue => issue.Code == "dns_asset_missing");

        var disconnected = definition with
        {
            Networks = definition.Networks.Select(network => network.Key == "core"
                ? network with { DnsServerAssetKey = "entry-web" }
                : network).ToArray()
        };
        Assert.Contains(new TeamLabTopologyValidator().Validate(disconnected, 2).Issues,
            issue => issue.Code == "dns_asset_missing");
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

    [Fact]
    public void Planner_PlacesDockerAndVmGroupsOnNodesWithTheirOwnStorageBudget()
    {
        var definition = new TeamLabTopologyDefinitionModel("split-storage",
            [Network("entry", "10.32.0.0/16", true), Network("vm", "10.33.0.0/16", false)],
            [
                Asset("docker", "entry", 1, 10) with
                { Resources = new TeamLabAssetResourceModel(10, 512, 80 * 1024) },
                Asset("guest", "vm", 2, 10) with
                {
                    Kind = TeamLabAssetKind.Vm,
                    Resources = new TeamLabAssetResourceModel(10, 512, 120 * 1024)
                }
            ], []);
        var execution = TeamLabTopologyV2Compiler.Compile(definition);
        var dockerNode = new TeamLabPlanningNodeSnapshot(Guid.NewGuid(), "docker-node", true, true,
            2, 2, 0, 0, new WorkloadResourceVector(80, 16_384, 50 * 1024, 2, 2),
            AvailableDockerStorageMiB: 90 * 1024);
        var vmNode = new TeamLabPlanningNodeSnapshot(Guid.NewGuid(), "vm-node", true, true,
            2, 2, 0, 0, new WorkloadResourceVector(80, 16_384, 150 * 1024, 2, 2),
            AvailableDockerStorageMiB: 50 * 1024);

        var placement = TeamLabAssetPlanner.BuildPlacement(execution, [dockerNode, vmNode]);

        Assert.NotNull(placement);
        Assert.Equal(2, placement.Count);
        Assert.Contains(placement, item => item.Node.Id == dockerNode.Id &&
            item.Groups.Single().AssetKeys.Contains("docker"));
        Assert.Contains(placement, item => item.Node.Id == vmNode.Id &&
            item.Groups.Single().AssetKeys.Contains("guest"));
    }

    [Fact]
    public void Planner_TreatsMeasuredZeroResourcesAsUnavailable()
    {
        var definition = new TeamLabTopologyDefinitionModel("measured-zero",
            [Network("entry", "10.34.0.0/16", true)],
            [Asset("docker", "entry", 1, 10)], []);
        var execution = TeamLabTopologyV2Compiler.Compile(definition);
        var node = new TeamLabPlanningNodeSnapshot(Guid.NewGuid(), "node", true, false,
            1, 0, 0, 0, AvailableDockerStorageMiB: 0, ResourceAvailabilityKnown: true);

        Assert.Null(TeamLabAssetPlanner.BuildPlacement(execution, [node]));
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
