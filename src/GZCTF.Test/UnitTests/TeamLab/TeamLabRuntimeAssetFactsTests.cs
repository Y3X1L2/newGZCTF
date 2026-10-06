using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRuntimeAssetFactsTests
{
    private const string Digest = "sha256:0123456789abcdef";

    [Fact]
    public async Task RuntimeDetailKeepsOrphanedRuntimeReadableWithoutCreatingAccessResources()
    {
        await using var context = Context();
        var runtime = new TeamLabRuntime { Id = 8, TopologyReleaseId = Guid.NewGuid(),
            Generation = 2, Status = TeamLabRuntimeStatus.Running };
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var detail = await new TeamLabRuntimeProjectionService(context)
            .GetAsync(runtime.PublicId, CancellationToken.None);

        Assert.Null(detail.TopologyId);
        Assert.Null(detail.TopologyName);
        Assert.Null(detail.ReleaseVersion);
        Assert.Equal(2, detail.Generation);
        Assert.Empty(detail.Assets);
        Assert.Empty(await context.TeamLabRemoteSessions.ToArrayAsync());
        Assert.Empty(await context.TeamLabAccessGrants.ToArrayAsync());
    }

    [Fact]
    public async Task RuntimeDetailUsesFrozenReleaseNameInsteadOfEditableSceneName()
    {
        await using var context = Context();
        var topology = new TeamLabTopology { Name = "Edited scene" };
        context.TeamLabTopologies.Add(topology);
        await context.SaveChangesAsync();
        var release = new TeamLabTopologyRelease { TopologyId = topology.Id, Version = 3,
            CanonicalJson = "{\"name\":\"Frozen scene\"}" };
        context.TeamLabTopologyReleases.Add(release);
        var runtime = new TeamLabRuntime { TopologyReleaseId = release.Id,
            Generation = 1, Status = TeamLabRuntimeStatus.Running };
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var detail = await new TeamLabRuntimeProjectionService(context)
            .GetAsync(runtime.PublicId, CancellationToken.None);

        Assert.Equal(topology.PublicId, detail.TopologyId);
        Assert.Equal("Frozen scene", detail.TopologyName);
        Assert.Equal(3, detail.ReleaseVersion);
    }

    [Fact]
    public void CurrentPlanRevisionProjectsEachInterfaceWithoutInventingObservation()
    {
        var runtime = new TeamLabRuntime { Id = 8, Generation = 4, PlanRevision = 2 };
        var asset = Asset();
        var old = Spec(TeamLabGuestOperatingSystem.Windows, "10.66.0.15", "172.22.1.14");
        var current = Spec(TeamLabGuestOperatingSystem.Windows, "10.66.0.15", "172.22.1.15");
        var snapshot = Snapshot(runtime, Plan(runtime, old), Plan(runtime, current));

        var specs = TeamLabRuntimeAssetFacts.ReadCurrentSpecs(runtime, [snapshot]);
        var interfaces = TeamLabRuntimeAssetFacts.Interfaces(asset, specs[asset.TopologyKey]);

        Assert.Equal(2, interfaces.Count);
        Assert.Equal("10.66.0.15", interfaces.Single(item => item.NetworkKey == "edge").Assigned?.IpAddress);
        var inside = interfaces.Single(item => item.NetworkKey == "inside");
        Assert.Equal("172.22.1.15", inside.Assigned?.IpAddress);
        Assert.Equal(23, inside.Assigned?.PrefixLength);
        Assert.Equal(["172.22.1.2"], inside.Assigned?.DnsServers);
        Assert.Null(inside.Assigned?.GatewayIp);
        Assert.Single(inside.Assigned!.StaticRoutes);
        Assert.Null(inside.Observed);
    }

    [Fact]
    public void ResetGenerationAndChangedAssetIdentityCannotReuseOldFacts()
    {
        var runtime = new TeamLabRuntime { Id = 8, Generation = 5, PlanRevision = 0 };
        var old = new TeamLabRuntime { Id = runtime.Id, PublicId = runtime.PublicId, Generation = 4 };
        var oldSnapshot = Snapshot(old, Plan(old, Spec(TeamLabGuestOperatingSystem.Windows,
            "10.66.0.15", "172.22.1.15")));
        var specs = TeamLabRuntimeAssetFacts.ReadCurrentSpecs(runtime, [oldSnapshot]);
        Assert.Empty(specs);
        Assert.Empty(TeamLabRuntimeAssetFacts.Interfaces(Asset(), null));

        var asset = Asset();
        var current = Spec(TeamLabGuestOperatingSystem.Windows, "10.66.0.15", "172.22.1.15");
        Assert.Empty(TeamLabRuntimeAssetFacts.Interfaces(asset, new(current with { ImageDigest = "other" }, true)));
        var changedNetwork = current with { NetworkAttachments =
            current.NetworkAttachments.Select(item => item.NetworkKey == "inside"
                ? item with { IpAddress = "172.22.1.99" } : item).ToArray() };
        var projected = TeamLabRuntimeAssetFacts.Interfaces(asset, new(changedNetwork, true));
        Assert.Null(projected.Single(item => item.NetworkKey == "inside").Assigned);
    }

    [Fact]
    public void MissingLinuxEnumUsesIdentifiedTemplateWithHonestSource()
    {
        var runtime = new TeamLabRuntime { Id = 8, Generation = 4 };
        var asset = Asset();
        var spec = Spec(TeamLabGuestOperatingSystem.Linux, "10.66.0.15", "172.22.1.15");
        var facts = TeamLabRuntimeAssetFacts.ReadCurrentSpecs(runtime,
            [Snapshot(runtime, Plan(runtime, spec))]);
        var executed = facts[asset.TopologyKey];
        Assert.False(executed.OperatingSystemRecorded);

        var template = new ImageTemplate { Id = 42, ImageHash = Digest, OSType = OSType.Linux };
        Assert.Equal(("linux", "template-current"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        template.OSType = OSType.Windows;
        Assert.Equal(("windows", "template-current"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        template.ImageHash = "changed-image";
        Assert.Equal(("unknown", "unknown"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        Assert.Equal(("unknown", "unknown"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, null));
    }

    [Fact]
    public void ExplicitWindowsPlanAndCapabilitiesDoNotClaimConnectionVerified()
    {
        var runtime = new TeamLabRuntime { Id = 8, Generation = 4 };
        var asset = Asset();
        var facts = TeamLabRuntimeAssetFacts.ReadCurrentSpecs(runtime,
            [Snapshot(runtime, Plan(runtime, Spec(TeamLabGuestOperatingSystem.Windows,
                "10.66.0.15", "172.22.1.15")))]);
        var os = TeamLabRuntimeAssetFacts.OperatingSystem(asset, facts[asset.TopologyKey], null);
        Assert.Equal(("windows", "execution-plan"), os);

        var configured = new ImageTemplateRemoteAccess { ImageTemplateId = 42, Enabled = true,
            Protocol = TeamLabRemoteProtocol.Rdp, Port = 3389,
            Username = "operator", ProtectedSecret = "protected-placeholder" };
        var capabilities = TeamLabRuntimeAssetFacts.Capabilities(asset, TeamLabRuntimeStatus.Running,
            os.Value, configured);
        Assert.Equal("configured-unverified", capabilities.Single(item => item.Kind == "rdp").Status);
        Assert.Equal(42, capabilities.Single(item => item.Kind == "rdp").SettingsTemplateId);
        Assert.Equal("unsupported", capabilities.Single(item => item.Kind == "files").Status);
        Assert.Equal("unsupported", capabilities.Single(item => item.Kind == "ssh").Status);

        asset.Status = TeamLabRuntimeStatus.Deploying;
        Assert.True(TeamLabRuntimeAssetFacts.IsRunning(asset, TeamLabRuntimeStatus.Running));
        Assert.Equal("configured-unverified", TeamLabRuntimeAssetFacts.Capabilities(asset,
            TeamLabRuntimeStatus.Running, os.Value, configured)
            .Single(item => item.Kind == "rdp").Status);
        asset.Status = TeamLabRuntimeStatus.Failed;
        Assert.False(TeamLabRuntimeAssetFacts.IsRunning(asset, TeamLabRuntimeStatus.Running));
    }

    private static TeamLabRuntimeAsset Asset() => new()
    {
        RuntimeId = 8, Generation = 4, Kind = TeamLabResourceKind.Vm,
        TopologyKey = "web", SourceTemplateId = 42, ImageDigest = Digest,
        WorkerNodeId = Guid.NewGuid(), RuntimeResourceId = "vm-web",
        NativeIdentity = Guid.NewGuid().ToString("D"), IpAddress = "10.66.0.15",
        Status = TeamLabRuntimeStatus.Running,
        InterfaceSummaryJson = JsonSerializer.Serialize(new[]
        {
            new { Key = "eth0", NetworkKey = "edge", IpAddress = "10.66.0.15",
                PrefixLength = 24, MacAddress = "02:42:00:00:00:01", Primary = true },
            new { Key = "eth1", NetworkKey = "inside", IpAddress = "172.22.1.15",
                PrefixLength = 23, MacAddress = "02:42:00:00:00:02", Primary = false }
        })
    };

    private static TeamLabAssetExecutionSpecV2 Spec(TeamLabGuestOperatingSystem os,
        string edgeIp, string insideIp) => new(
        "web", "vm", "vm-web", Digest, "vm-web", 42, 2, 2048,
        [
            new("edge", "web:eth0", "eth0", edgeIp, "10.66.0.1", true,
                InterfaceKey: "eth0", PrefixLength: 24, DnsServers: ["10.66.0.2"]),
            new("inside", "web:eth1", "eth1", insideIp, null, false,
                InterfaceKey: "eth1", PrefixLength: 23, DnsServers: ["172.22.1.2"],
                StaticRoutes: [new TeamLabGuestRouteV2("172.30.0.0/16", "172.22.1.1")])
        ], [], OperatingSystem: os);

    private static TeamLabExecutionPlanV2 Plan(TeamLabRuntime runtime,
        TeamLabAssetExecutionSpecV2 spec) => new(runtime.Id, runtime.PublicId,
        runtime.Generation, "shard", "digest", "network-digest", true,
        [], [spec], []);

    private static TeamLabExecutionPlanSnapshot Snapshot(TeamLabRuntime runtime,
        TeamLabExecutionPlanV2 original, TeamLabExecutionPlanV2? current = null) => new()
    {
        RuntimeId = runtime.Id, Generation = runtime.Generation, ShardId = 1,
        PlanJson = JsonSerializer.Serialize(original),
        CurrentPlanJson = current is null ? null : JsonSerializer.Serialize(current)
    };

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
