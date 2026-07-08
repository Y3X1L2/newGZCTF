using System;
using System.Linq;
using GZCTF.Models.Data;
using GZCTF.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabShardPlannerTests
{
    [Fact]
    public void PlanShards_KeepsSingleNodePlanWhenCapacityIsEnough()
    {
        var node = CreateNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "node-a", maxContainers: 6, maxVms: 2);
        var networks = new[]
        {
            Network("entry", "Entry", "10.10.0.0/24"),
            Network("data", "Data", "10.20.0.0/24")
        };
        var assets = new[]
        {
            Asset("web", "entry", TeamLabAssetSpecKind.Docker),
            Asset("db", "data", TeamLabAssetSpecKind.Docker)
        };

        var result = TeamLabShardPlanner.PlanShards(networks, assets, [node]);

        Assert.True(result.Success, result.Message);
        var shard = Assert.Single(result.Shards);
        Assert.Equal(node.Id, shard.WorkerNodeId);
        Assert.Equal(["entry", "data"], shard.NetworkKeys);
        Assert.Equal(["db", "web"], shard.AssetKeys);
    }

    [Fact]
    public void PlanShards_SplitsByNetworkWhenSingleNodeCapacityIsInsufficient()
    {
        var nodeA = CreateNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "node-a", maxContainers: 2, maxVms: 0);
        var nodeB = CreateNode("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "node-b", maxContainers: 2, maxVms: 0);
        var networks = new[]
        {
            Network("entry", "Entry", "10.10.0.0/24"),
            Network("data", "Data", "10.20.0.0/24")
        };
        var assets = new[]
        {
            Asset("web-a", "entry", TeamLabAssetSpecKind.Docker),
            Asset("web-b", "entry", TeamLabAssetSpecKind.Docker),
            Asset("db-a", "data", TeamLabAssetSpecKind.Docker),
            Asset("db-b", "data", TeamLabAssetSpecKind.Docker)
        };

        var result = TeamLabShardPlanner.PlanShards(networks, assets, [nodeA, nodeB]);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Shards.Count);
        Assert.All(result.Shards, shard => Assert.Single(shard.NetworkKeys));
        Assert.Contains(result.Shards, shard => shard.NetworkKeys.SequenceEqual(["entry"]));
        Assert.Contains(result.Shards, shard => shard.NetworkKeys.SequenceEqual(["data"]));
    }

    [Fact]
    public void PlanShards_AllowsDockerOnlyFabricNodesForDockerOnlyShard()
    {
        var dockerOnlyNode = CreateNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "docker-node", maxContainers: 8, maxVms: 0);
        dockerOnlyNode.Capabilities = NodeCapability.Docker;
        var kvmNode = CreateNode("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "kvm-node", maxContainers: 8, maxVms: 1);
        kvmNode.CpuLoad = 0.8f;
        kvmNode.MemoryLoad = 0.8f;
        var networks = new[]
        {
            Network("entry", "Entry", "10.10.0.0/24"),
            Network("data", "Data", "10.20.0.0/24")
        };
        var assets = new[]
        {
            Asset("web", "entry", TeamLabAssetSpecKind.Docker),
            Asset("db", "data", TeamLabAssetSpecKind.Docker)
        };

        var result = TeamLabShardPlanner.PlanShards(networks, assets, [dockerOnlyNode, kvmNode]);

        Assert.True(result.Success, result.Message);
        var shard = Assert.Single(result.Shards);
        Assert.Equal(dockerOnlyNode.Id, shard.WorkerNodeId);
    }

    [Fact]
    public void PlanShards_RequiresKvmCapabilityForVmShard()
    {
        var dockerOnlyNode = CreateNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "docker-node", maxContainers: 8, maxVms: 1);
        dockerOnlyNode.Capabilities = NodeCapability.Docker;
        var kvmNode = CreateNode("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "kvm-node", maxContainers: 8, maxVms: 1);
        kvmNode.CpuLoad = 0.8f;
        kvmNode.MemoryLoad = 0.8f;
        var networks = new[] { Network("ops", "Ops", "10.30.0.0/24") };
        var assets = new[] { Asset("ops-vm", "ops", TeamLabAssetSpecKind.Vm) };

        var result = TeamLabShardPlanner.PlanShards(networks, assets, [dockerOnlyNode, kvmNode]);

        Assert.True(result.Success, result.Message);
        var shard = Assert.Single(result.Shards);
        Assert.Equal(kvmNode.Id, shard.WorkerNodeId);
    }

    [Fact]
    public void PlanShards_KeepsMultiInterfaceAssetsOnOneShardWithTheirNetworks()
    {
        var nodeA = CreateNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "node-a", maxContainers: 3, maxVms: 0);
        var nodeB = CreateNode("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "node-b", maxContainers: 3, maxVms: 0);
        var networks = new[]
        {
            Network("entry", "Entry", "10.10.0.0/24"),
            Network("data", "Data", "10.20.0.0/24"),
            Network("ops", "Ops", "10.30.0.0/24")
        };
        var assets = new[]
        {
            Asset("web", "entry", TeamLabAssetSpecKind.Docker),
            Asset("db", "data", TeamLabAssetSpecKind.Docker),
            Asset("ops", "ops", TeamLabAssetSpecKind.Docker),
            MultiInterfaceAsset("router", ["entry", "data"], TeamLabAssetSpecKind.Docker)
        };

        var result = TeamLabShardPlanner.PlanShards(networks, assets, [nodeA, nodeB]);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Shards.Count);
        var routedShard = Assert.Single(result.Shards,
            shard => shard.NetworkKeys.Contains("entry") || shard.NetworkKeys.Contains("data"));
        Assert.Equal(["data", "entry"], routedShard.NetworkKeys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Contains("router", routedShard.AssetKeys);
        Assert.Single(result.Shards.SelectMany(shard => shard.AssetKeys), key => key == "router");
    }

    [Fact]
    public void PlanShards_RejectsSingleNetworkWhenItExceedsAnyNodeCapacity()
    {
        var nodeA = CreateNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "node-a", maxContainers: 2, maxVms: 0);
        var nodeB = CreateNode("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "node-b", maxContainers: 2, maxVms: 0);
        var networks = new[] { Network("entry", "Entry", "10.10.0.0/24") };
        var assets = new[]
        {
            Asset("web-a", "entry", TeamLabAssetSpecKind.Docker),
            Asset("web-b", "entry", TeamLabAssetSpecKind.Docker),
            Asset("web-c", "entry", TeamLabAssetSpecKind.Docker)
        };

        var result = TeamLabShardPlanner.PlanShards(networks, assets, [nodeA, nodeB]);

        Assert.False(result.Success);
        Assert.Contains("single network", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Shards);
    }

    [Fact]
    public void PlanShards_IsDeterministicForSameInputs()
    {
        var nodeA = CreateNode("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "node-a", maxContainers: 2, maxVms: 1);
        var nodeB = CreateNode("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "node-b", maxContainers: 2, maxVms: 1);
        var networks = new[]
        {
            Network("entry", "Entry", "10.10.0.0/24"),
            Network("ops", "Ops", "10.30.0.0/24"),
            Network("data", "Data", "10.20.0.0/24")
        };
        var assets = new[]
        {
            Asset("web", "entry", TeamLabAssetSpecKind.Docker),
            Asset("ops", "ops", TeamLabAssetSpecKind.Vm),
            Asset("db", "data", TeamLabAssetSpecKind.Docker)
        };

        var first = TeamLabShardPlanner.PlanShards(networks, assets, [nodeB, nodeA]);
        var second = TeamLabShardPlanner.PlanShards(networks.Reverse().ToArray(), assets.Reverse().ToArray(),
            [nodeA, nodeB]);

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.Equal(
            Normalize(first),
            Normalize(second));
    }

    static TeamLabRuntimeNetworkSpec Network(string key, string name, string cidr) =>
        new(key, name, cidr, cidr.Replace(".0/24", ".1"), $"tl-{key}");

    static TeamLabAssetSpec Asset(string key, string networkKey, TeamLabAssetSpecKind kind) =>
        new(
            kind,
            TopologyKey: key,
            Name: key,
            SourceTemplateId: 1,
            Image: $"{key}:latest",
            CpuCount: 1,
            MemoryLimit: 128,
            StorageLimit: 128,
            ExposePort: 80,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces:
            [
                new TeamLabAssetInterfaceSpec(
                    NodeKey: key,
                    NetworkKey: networkKey,
                    BridgeName: $"tl-{networkKey}",
                    InterfaceName: "eth0",
                    IpAddress: "10.10.0.10",
                    PrefixLength: 24,
                    MacAddress: "02:42:00:00:00:01",
                    IsPrimary: true,
                    RemoveDefaultRoute: false)
            ]);

    static TeamLabAssetSpec MultiInterfaceAsset(string key, string[] networkKeys, TeamLabAssetSpecKind kind) =>
        new(
            kind,
            TopologyKey: key,
            Name: key,
            SourceTemplateId: 1,
            Image: $"{key}:latest",
            CpuCount: 1,
            MemoryLimit: 128,
            StorageLimit: 128,
            ExposePort: 80,
            InfrastructureRole: null,
            StartPriority: 50,
            Interfaces: networkKeys
                .Select((networkKey, index) => new TeamLabAssetInterfaceSpec(
                    NodeKey: key,
                    NetworkKey: networkKey,
                    BridgeName: $"tl-{networkKey}",
                    InterfaceName: $"eth{index}",
                    IpAddress: $"10.{index + 10}.0.10",
                    PrefixLength: 24,
                    MacAddress: $"02:42:00:00:00:{index + 1:00}",
                    IsPrimary: index == 0,
                    RemoveDefaultRoute: false))
                .ToArray());

    static WorkerNode CreateNode(string id, string name, int maxContainers, int maxVms) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        HostAddress = name,
        Status = NodeStatus.Online,
        IsLocal = true,
        IsSchedulable = true,
        Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
        MaxContainers = maxContainers,
        MaxVms = maxVms,
        CpuLoad = 0.1f,
        MemoryLoad = 0.1f,
        TeamLabNetworkEnabled = true,
        TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
        TeamLabTunnelIp = id.StartsWith('a') ? "10.250.0.2" : "10.250.0.3",
        TeamLabAgentVersion = "1.8.3",
        TeamLabProtocolVersion = 3,
        TeamLabFabricStatus = TeamLabFabricStatus.Healthy,
        TeamLabFabricIp = id.StartsWith('a') ? "10.251.0.2" : "10.251.0.3",
        TeamLabCapabilitiesJson = """{"docker":true,"wireGuard":true,"iptables":true}"""
    };

    static (Guid WorkerNodeId, string Networks, string Assets)[] Normalize(TeamLabShardPlanResult result) =>
        result.Shards
            .Select(shard => (
                shard.WorkerNodeId,
                Networks: string.Join(",", shard.NetworkKeys.OrderBy(key => key, StringComparer.Ordinal)),
                Assets: string.Join(",", shard.AssetKeys.OrderBy(key => key, StringComparer.Ordinal))))
            .OrderBy(item => item.WorkerNodeId)
            .ThenBy(item => item.Networks, StringComparer.Ordinal)
            .ToArray();
}
