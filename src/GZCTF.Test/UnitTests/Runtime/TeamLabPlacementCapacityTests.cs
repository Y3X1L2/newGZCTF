using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class TeamLabPlacementCapacityTests
{
    readonly ITestOutputHelper _output;

    public TeamLabPlacementCapacityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Placement_UsesDeclaredResourcesAcrossHeterogeneousNodes()
    {
        await using var context = CreateContext();
        var small = SeedNode(context, "node-small", 4, 4, 10);
        var large = SeedNode(context, "node-large", 16, 32, 10);
        var runtime = SeedRuntime(context,
            [Asset("workload", "entry", new TeamLabAssetResourceModel(80, 8_192, 2_048))]);
        var ticket = SeedTicket(context, runtime.Id, dockerSlots: 1);

        var result = await CreatePlacement(context)
            .BindAndReserveAsync(ticket.Id, runtime.Id, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(large.Id, result.Node?.Id);
        Assert.NotEqual(small.Id, result.Node?.Id);
        var reservation = Assert.Single(await context.FleetCapacityReservations.ToArrayAsync());
        Assert.Equal((80L, 8_192L, 2_048L, 1, 0),
            (reservation.CpuUnits, reservation.MemoryMiB, reservation.StorageMiB,
                reservation.DockerSlots, reservation.VmSlots));
    }

    [Fact]
    public async Task Placement_NeverSplitsOneNetworkAcrossNodes()
    {
        await using var context = CreateContext();
        SeedNode(context, "node-a", 8, 16, 1);
        SeedNode(context, "node-b", 8, 16, 1);
        var runtime = SeedRuntime(context,
        [
            Asset("first", "entry", new TeamLabAssetResourceModel(10, 256, 512)),
            Asset("second", "entry", new TeamLabAssetResourceModel(10, 256, 512))
        ]);
        var ticket = SeedTicket(context, runtime.Id, dockerSlots: 2);

        var result = await CreatePlacement(context)
            .BindAndReserveAsync(ticket.Id, runtime.Id, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("single_network_capacity_exceeded", result.Message, StringComparison.Ordinal);
        Assert.Empty(await context.TeamLabRuntimeShards.ToArrayAsync());
        Assert.Empty(await context.FleetCapacityReservations.ToArrayAsync());
        Assert.All(await context.TeamLabRuntimeAssets.ToArrayAsync(), item => Assert.Null(item.WorkerNodeId));
    }

    [Fact]
    public async Task Placement_UsesStableNodeNameTieBreak()
    {
        await using var context = CreateContext();
        var expected = SeedNode(context, "node-a", 8, 16, 2,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        SeedNode(context, "node-b", 8, 16, 2,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var runtime = SeedRuntime(context,
            [Asset("workload", "entry", new TeamLabAssetResourceModel(10, 256, 512))]);
        var ticket = SeedTicket(context, runtime.Id, dockerSlots: 1);

        var result = await CreatePlacement(context)
            .BindAndReserveAsync(ticket.Id, runtime.Id, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(expected.Id, result.Node?.Id);
    }

    [Fact]
    public async Task FailedMultiGroupPlacement_PersistsNoPartialAssignment()
    {
        await using var context = CreateContext();
        SeedNode(context, "node-a", 8, 16, 1);
        var runtime = SeedRuntime(context,
        [
            Asset("entry-service", "entry", new TeamLabAssetResourceModel(10, 256, 512)),
            Asset("core-service", "core", new TeamLabAssetResourceModel(10, 256, 512))
        ]);
        var ticket = SeedTicket(context, runtime.Id, dockerSlots: 2);

        var result = await CreatePlacement(context)
            .BindAndReserveAsync(ticket.Id, runtime.Id, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(await context.TeamLabRuntimeShards.ToArrayAsync());
        Assert.Empty(await context.FleetCapacityReservations.ToArrayAsync());
        Assert.All(await context.TeamLabRuntimeNetworks.ToArrayAsync(), item => Assert.Null(item.WorkerNodeId));
    }

    [Fact]
    public async Task Placement_32NetworksAnd128Assets_IsDeterministicAndBounded()
    {
        var first = await RunLargePlacementAsync();
        var second = await RunLargePlacementAsync();

        _output.WriteLine("first={0:F1}ms second={1:F1}ms hash={2}",
            first.Elapsed.TotalMilliseconds, second.Elapsed.TotalMilliseconds, first.Hash);

        Assert.Equal(first.Hash, second.Hash);
        Assert.True(first.Elapsed < TimeSpan.FromSeconds(2),
            $"Large placement exceeded the local algorithm gate: {first.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.True(second.Elapsed < TimeSpan.FromSeconds(2),
            $"Large placement exceeded the local algorithm gate: {second.Elapsed.TotalMilliseconds:F1} ms.");
    }

    static async Task<(TimeSpan Elapsed, string Hash)> RunLargePlacementAsync()
    {
        await using var context = CreateContext();
        for (var index = 0; index < 8; index++)
            SeedNode(context, $"node-{index:D2}", 16, 64, 32,
                Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"));

        var assets = Enumerable.Range(0, 32)
            .SelectMany(network => Enumerable.Range(0, 4)
                .Select(asset => Asset(
                    $"asset-{network:D2}-{asset:D2}",
                    network == 0 ? "entry" : $"network-{network:D2}",
                    new TeamLabAssetResourceModel(5, 128, 256))))
            .ToArray();
        var runtime = SeedRuntime(context, assets);
        var ticket = SeedTicket(context, runtime.Id, dockerSlots: assets.Length);

        var stopwatch = Stopwatch.StartNew();
        var result = await CreatePlacement(context)
            .BindAndReserveAsync(ticket.Id, runtime.Id, CancellationToken.None);
        stopwatch.Stop();
        Assert.True(result.Success, result.Message);

        var placement = await context.TeamLabRuntimeNetworks.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id)
            .OrderBy(item => item.TopologyKey)
            .Select(item => $"{item.TopologyKey}:{item.WorkerNodeId}")
            .ToArrayAsync();
        var hash = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('|', placement))));
        return (stopwatch.Elapsed, hash);
    }

    static TeamLabRuntime SeedRuntime(AppDbContext context, IReadOnlyList<TeamLabTopologyAssetModel> assets)
    {
        var networkKeys = assets.SelectMany(asset => asset.Interfaces)
            .Select(item => item.NetworkKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var definition = new TeamLabTopologyDefinitionModel(
            "placement-capacity",
            networkKeys.Select((key, index) => new TeamLabTopologyNetworkModel(
                key, key, new TeamLabAddressPoolModel($"10.{60 + index}.0.0/16", 24),
                string.Equals(key, "entry", StringComparison.Ordinal))).ToArray(),
            assets,
            []);
        var canonical = TeamLabReleaseCodec.Encode(2, definition);
        var topology = new TeamLabTopology { Name = $"placement-{Guid.NewGuid():N}", OwnerUserId = Guid.NewGuid() };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology,
            Version = 1,
            SourceRevision = 1,
            SchemaVersion = 2,
            CanonicalJson = canonical,
            ContentHash = TeamLabReleaseCodec.ComputeContentHash(2, canonical)
        };
        var runtime = new TeamLabRuntime
        {
            Id = 12_000,
            TopologyReleaseId = release.Id,
            Status = TeamLabRuntimeStatus.Scheduled
        };
        foreach (var (key, index) in networkKeys.Select((key, index) => (key, index)))
        {
            runtime.Networks.Add(new TeamLabRuntimeNetwork
            {
                Generation = 1,
                TopologyKey = key,
                PlacementGroupKey = key,
                Name = key,
                Cidr = $"10.{60 + index}.1.0/24",
                GatewayIp = $"10.{60 + index}.1.1",
                BridgeName = $"tl-{key}",
                IsEntry = string.Equals(key, "entry", StringComparison.Ordinal)
            });
        }
        foreach (var asset in assets)
        {
            var networkKey = asset.Interfaces.Single().NetworkKey;
            runtime.Assets.Add(new TeamLabRuntimeAsset
            {
                Generation = 1,
                PlacementGroupKey = networkKey,
                TopologyKey = asset.Key,
                Name = asset.Name,
                Kind = asset.Kind == TeamLabAssetKind.Docker
                    ? TeamLabResourceKind.Docker
                    : TeamLabResourceKind.Vm
            });
        }
        context.AddRange(release, runtime);
        context.SaveChanges();
        return runtime;
    }

    static DeploymentQueueTicket SeedTicket(AppDbContext context, int runtimeId, int dockerSlots)
    {
        var ticket = DeploymentQueueTicket.Create(
            DeploymentQueueRequest.TeamLab(runtimeId, dockerSlots, 0));
        context.DeploymentQueueTickets.Add(ticket);
        context.SaveChanges();
        return ticket;
    }

    static TeamLabTopologyAssetModel Asset(
        string key,
        string networkKey,
        TeamLabAssetResourceModel resources) => new(
        key,
        key,
        TeamLabAssetKind.Docker,
        1,
        resources,
        [new TeamLabTopologyInterfaceModel("eth0", networkKey, 10, true)],
        ExposePort: null);

    static WorkerNode SeedNode(
        AppDbContext context,
        string name,
        int logicalCpu,
        int memoryGiB,
        int maxContainers,
        Guid? id = null)
    {
        var manifest = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "placement-test", null, 1,
            [
                AgentFeatureIds.Docker,
                AgentFeatureIds.DockerPull,
                AgentFeatureIds.TeamLabInfrastructure,
                AgentFeatureIds.TeamLabFabricLeasedLinks,
                AgentFeatureIds.TeamLabObservation,
                AgentFeatureIds.WireGuard
            ],
            new AgentExecutionLimits(4, 0, 2, 0, 4, 2),
            new AgentHostFacts(logicalCpu, memoryGiB * 1024L * 1024 * 1024,
                100L * 1024 * 1024 * 1024),
            DateTimeOffset.UtcNow));
        var node = new WorkerNode
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            HostAddress = "127.0.0.1",
            AuthToken = "token",
            IsLocal = true,
            IsSchedulable = true,
            Status = NodeStatus.Online,
            Capabilities = NodeCapability.Docker,
            MaxContainers = maxContainers,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabFabricStatus = TeamLabFabricStatus.Healthy,
            TeamLabTunnelIp = "10.251.0.2",
            TeamLabFabricIp = "10.251.0.2",
            CapabilityManifestJson = manifest.Json,
            CapabilityManifestSchemaVersion = 1,
            CapabilityHash = manifest.Hash
        };
        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    static TeamLabPhysicalPlacementService CreatePlacement(AppDbContext context)
    {
        var lease = new LocalDevelopmentLeaseProvider();
        var options = Options.Create(new RuntimeSchedulingOptions());
        var snapshots = new NodeCapacitySnapshotService(context);
        var eligibility = new NodeEligibilityEvaluator(options);
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        return new TeamLabPhysicalPlacementService(
            context,
            lease,
            snapshots,
            eligibility,
            writer,
            new TeamLabEventRecorder(context, writer, new OperationalCorrelation()),
            new TeamLabFabricLinkAllocator(context, Options.Create(new TeamLabNetworkConfig())),
            Options.Create(new TeamLabNetworkConfig()),
            options);
    }

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
