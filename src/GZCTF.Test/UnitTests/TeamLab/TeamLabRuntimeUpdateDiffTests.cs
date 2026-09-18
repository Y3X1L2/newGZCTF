using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRuntimeUpdateDiffTests
{
    [Fact]
    public void BuildChanges_ReportsAddedRemovedAndReplacedAssets()
    {
        var current = Topology(Asset("keep"), Asset("replace", templateId: 1), Asset("remove"));
        var target = Topology(Asset("keep"), Asset("replace", templateId: 2), Asset("add"));

        var changes = TeamLabRuntimeUpdateService.BuildChanges(current, target);

        Assert.Contains(changes, item => item is { AssetKey: "add", Action: "add" });
        Assert.Contains(changes, item => item is { AssetKey: "replace", Action: "replace" });
        Assert.Contains(changes, item => item is { AssetKey: "remove", Action: "remove" });
        Assert.DoesNotContain(changes, item => item.AssetKey == "keep");
    }

    [Fact]
    public void SameTopologyStructure_AllowsAssetChangesButRejectsNetworkChanges()
    {
        var current = Topology(Asset("one"));
        var assetChanged = Topology(Asset("two"));
        var networkChanged = current with
        {
            Networks = [current.Networks[0] with { AddressPoolCidr = "10.20.0.0/16" }]
        };

        Assert.True(TeamLabRuntimeUpdateService.SameTopologyStructure(current, assetChanged));
        Assert.False(TeamLabRuntimeUpdateService.SameTopologyStructure(current, networkChanged));
    }

    [Fact]
    public void SameConnectorBindings_RejectsRemovalAndRebinding()
    {
        var connector = Guid.NewGuid();
        var current = Topology(Asset("field", connectorId: connector));

        Assert.False(TeamLabRuntimeUpdateService.SameConnectorBindings(current, Topology()));
        Assert.False(TeamLabRuntimeUpdateService.SameConnectorBindings(
            current,
            Topology(Asset("field", connectorId: Guid.NewGuid()))));
        Assert.True(TeamLabRuntimeUpdateService.SameConnectorBindings(
            current,
            Topology(Asset("field", connectorId: connector), Asset("ordinary"))));
    }

    [Fact]
    public void BuildOverlayValues_AcceptsOnlyAssetsCreatedByTheUpdate()
    {
        var changes = TeamLabRuntimeUpdateService.BuildChanges(
            Topology(Asset("keep")),
            Topology(Asset("keep"), Asset("add")));
        var accepted = TeamLabRuntimeUpdateService.BuildOverlayValues(
            [new TeamLabRuntimeOverlayModel("add", new Dictionary<string, string> { ["FLAG"] = "value" })],
            changes);

        Assert.Contains("add", accepted.Keys);
        var exception = Assert.Throws<TeamLabApiContractException>(() =>
            TeamLabRuntimeUpdateService.BuildOverlayValues(
                [new TeamLabRuntimeOverlayModel("keep", null)],
                changes));
        Assert.Equal("runtime_update_overlay_not_applicable", exception.Code);
    }

    [Fact]
    public void SyncWorkloadObservationPoints_CoversDockerVmAndRemovedAssets()
    {
        var nodeId = Guid.NewGuid();
        var runtime = new TeamLabRuntime
        {
            Id = 42,
            PublicId = Guid.NewGuid(),
            Generation = 3,
            Networks = [new TeamLabRuntimeNetwork { Id = 7, Generation = 3, TopologyKey = "lan" }]
        };
        runtime.Assets.AddRange(
        [
            RuntimeAsset(1, "docker", TeamLabResourceKind.Docker, nodeId),
            RuntimeAsset(2, "vm", TeamLabResourceKind.Vm, nodeId)
        ]);
        runtime.ObservationPoints.Add(new TeamLabObservationPoint
        {
            AssetId = 9,
            Generation = 3,
            Kind = TeamLabObservationPointKind.WorkloadEndpoint,
            TopologyKey = "removed",
            InterfaceToken = "old",
            Enabled = true
        });

        TeamLabRuntimeUpdateService.SyncWorkloadObservationPoints(runtime,
        [
            new("docker", "Docker", TeamLabAssetKind.Docker, "add"),
            new("vm", "VM", TeamLabAssetKind.Vm, "replace"),
            new("removed", "Removed", TeamLabAssetKind.Docker, "remove")
        ]);

        Assert.False(runtime.ObservationPoints.Single(item => item.TopologyKey == "removed").Enabled);
        Assert.Contains(runtime.ObservationPoints, item => item.AssetId == 1 && item.Enabled &&
            item.InterfaceToken == TeamLabExecutionIdentityV2.WorkloadHostInterface(runtime.PublicId, 3, "docker", "lan"));
        Assert.Contains(runtime.ObservationPoints, item => item.AssetId == 2 && item.Enabled &&
            item.InterfaceToken == TeamLabExecutionIdentityV2.VmTapName(runtime.PublicId, 3, "vm", "lan"));
    }

    static TeamLabRuntimeAsset RuntimeAsset(int id, string key, TeamLabResourceKind kind, Guid nodeId) => new()
    {
        Id = id,
        Generation = 3,
        ShardId = 5,
        WorkerNodeId = nodeId,
        Kind = kind,
        TopologyKey = key,
        Status = TeamLabRuntimeStatus.Running,
        InterfaceSummaryJson = JsonSerializer.Serialize(new[] { new { NetworkKey = "lan" } })
    };

    static TeamLabExecutionTopology Topology(params TeamLabExecutionAsset[] assets) => new(
        2,
        "runtime-update",
        [new TeamLabExecutionNetwork("lan", "LAN", "10.10.0.0/16", 24, true, 0)],
        [],
        assets,
        [],
        new TeamLabExecutionObservationPolicy(true, true));

    static TeamLabExecutionAsset Asset(
        string key,
        int templateId = 1,
        Guid? connectorId = null) => new(
        key,
        key,
        TeamLabAssetKind.Docker,
        templateId,
        100,
        256,
        512,
        [new TeamLabExecutionInterface("eth0", "lan", 10, true, 0)],
        null,
        null,
        null,
        0,
        ConnectorId: connectorId);
}
