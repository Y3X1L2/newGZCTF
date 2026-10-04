using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabManagedDnsUpdateTests
{
    [Fact]
    public async Task RuntimePreviewAndDeploymentCompiler_ReconcileInheritedDnsAfterProviderAddressChanges()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var template = new ImageTemplate
        {
            Id = 1, Name = "windows-network-fixture", OSType = OSType.Windows, ImageType = ImageType.Qcow2,
            VmNetworkMode = VmNetworkMode.ManagedStatic, ImageHash = "sha256:" + new string('a', 64)
        };
        var worker = new WorkerNode
        {
            Name = "worker", TeamLabFabricIp = "10.250.0.10",
            CapabilityManifestJson = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest("new", null, 1,
                [AgentFeatureIds.TeamLabManagedGuestNetwork], new(1, 1, 1, 1), new(4, 4L * 1024 * 1024 * 1024), DateTimeOffset.UtcNow)).Json
        };
        var source = new TeamLabTopologyDefinitionModel("DNS update",
            [new("lan", "LAN", new("10.48.0.0/16", 24), true, DnsServerAssetKey: "dc")],
            [Vm("dc", 10), Vm("member", 30)], []);
        var target = source with { Assets = [Vm("dc", 20), Vm("member", 30)] };
        var sourceRelease = Release(source);
        var targetRelease = Release(target);
        var current = TeamLabTopologyV2Compiler.Compile(source);
        var desired = TeamLabTopologyV2Compiler.Compile(target);
        var runtime = new TeamLabRuntime
        {
            Id = 42, Generation = 1, PublicId = Guid.NewGuid(), Status = TeamLabRuntimeStatus.Running,
            TopologyReleaseId = sourceRelease.Id,
            Shards = [new() { Id = 7, Generation = 1, WorkerNodeId = worker.Id }],
            Networks = [new() { Id = 9, Generation = 1, ShardId = 7, WorkerNodeId = worker.Id, TopologyKey = "lan",
                Cidr = "10.48.0.0/24", GatewayIp = "10.48.0.1", BridgeName = "br-lan", IsEntry = true }],
            Assets = current.Assets.Select((asset, index) => new TeamLabRuntimeAsset
            {
                Id = index + 1, Generation = 1, ShardId = 7, WorkerNodeId = worker.Id, TopologyKey = asset.Key, Name = asset.Name,
                Kind = TeamLabResourceKind.Vm, Status = TeamLabRuntimeStatus.Running, SourceTemplateId = 1,
                ImageDigest = template.ImageHash, ExecutionPlanJson = JsonSerializer.Serialize(asset),
                InterfaceSummaryJson = Interfaces(asset)
            }).ToList()
        };
        context.ImageTemplates.Add(template);
        context.WorkerNodes.Add(worker);
        context.TeamLabTopologyReleases.AddRange(sourceRelease, targetRelease);
        context.TeamLabRuntimes.Add(runtime);
        context.TeamLabFabricLinkLeases.Add(new()
        {
            RuntimeId = runtime.Id, Generation = 1, ShardId = 7, WorkerNodeId = worker.Id,
            AllocatedCidr = new IPNetwork(IPAddress.Parse("169.254.0.0"), 30),
            HubAddress = "169.254.0.1", NodeAddress = "169.254.0.2"
        });
        await context.SaveChangesAsync();
        var updater = new TeamLabRuntimeUpdateService(context, payloads: null!, topologyValidator: null!, queue: null!,
            capacity: null!, lifecycleGuard: new(context), deployment: null!, nodes: null!, assetControl: null!, artifacts: null!,
            remoteAccess: null!, serviceAccess: null!, traffic: null!, events: null!, logger: NullLogger<TeamLabRuntimeUpdateService>.Instance);

        var preview = await updater.PreviewAsync(runtime.PublicId, targetRelease.Id, CancellationToken.None);
        Assert.True(preview.CanApply, preview.ResetRequiredReason);
        Assert.Equal(["dc", "member"], preview.Changes.Select(change => change.AssetKey));
        Assert.All(preview.Changes, change => Assert.Equal("replace", change.Action));

        var compiler = new TeamLabShardDeploymentService(context, null!, null!, null!,
            new TeamLabRouteApplicationService(context, null!, null!), null!, null!, NullLogger<TeamLabShardDeploymentService>.Instance);
        var templates = new Dictionary<int, ImageTemplate> { [1] = template };
        var before = (await compiler.CompileExecutionPlansAsync(runtime, current, runtime.Assets, templates,
            new Dictionary<string, TeamLabRuntimeOverlayModel>(), CancellationToken.None))[7];
        Assert.Equal(["10.48.0.10"], before.Assets.Single(asset => asset.AssetKey == "member").NetworkAttachments[0].DnsServers);
        // Asset replacement allocates the declared new address before compiling the desired execution plan.
        var dc = runtime.Assets.Single(asset => asset.TopologyKey == "dc");
        dc.InterfaceSummaryJson = Interfaces(desired.Assets[0]);
        dc.ExecutionPlanJson = JsonSerializer.Serialize(desired.Assets[0]);
        var after = (await compiler.CompileExecutionPlansAsync(runtime, desired, runtime.Assets, templates,
            new Dictionary<string, TeamLabRuntimeOverlayModel>(), CancellationToken.None))[7];
        Assert.True(after.IsValid(out var error), error);
        Assert.Equal(["10.48.0.20"], after.Assets.Single(asset => asset.AssetKey == "member").NetworkAttachments[0].DnsServers);
        Assert.Empty(after.Networks[0].DhcpLeases!);
    }

    static TeamLabTopologyAssetModel Vm(string key, int host) => new(key, key, TeamLabAssetKind.Vm, 1,
        new(10, 512, 512), [new("nic", "lan", host, true, UseDefaultGateway: false)], VmNetworkMode: VmNetworkMode.ManagedStatic);

    static TeamLabTopologyRelease Release(TeamLabTopologyDefinitionModel definition) => new()
    {
        TopologyId = 1, SchemaVersion = 2, CanonicalJson = TeamLabReleaseCodec.Encode(2, definition)
    };

    static string Interfaces(TeamLabExecutionAsset asset) => JsonSerializer.Serialize(asset.Interfaces.Select(iface => new
    {
        iface.Key, iface.NetworkKey, IpAddress = $"10.48.0.{iface.HostOffset}", PrefixLength = 24,
        MacAddress = $"02:00:00:00:00:{iface.HostOffset:x2}", iface.Primary
    }));
}
