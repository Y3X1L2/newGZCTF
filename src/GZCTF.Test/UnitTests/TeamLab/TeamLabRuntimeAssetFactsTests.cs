using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRuntimeAssetFactsTests
{
    private const string RawDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Digest = "sha256:" + RawDigest;

    [Fact]
    public async Task RuntimeDetailKeepsOrphanedRuntimeReadableWithoutCreatingAccessResources()
    {
        await using var context = Context();
        var runtime = new TeamLabRuntime { Id = 8, TopologyReleaseId = Guid.NewGuid(),
            Generation = 2, Status = TeamLabRuntimeStatus.Running };
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var detail = await new TeamLabRuntimeProjectionService(context, new EfImageRuntimeAccessQuery(context))
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

        var detail = await new TeamLabRuntimeProjectionService(context, new EfImageRuntimeAccessQuery(context))
            .GetAsync(runtime.PublicId, CancellationToken.None);

        Assert.Equal(topology.PublicId, detail.TopologyId);
        Assert.Equal("Frozen scene", detail.TopologyName);
        Assert.Equal(3, detail.ReleaseVersion);
    }

    [Fact]
    public async Task RuntimeDetailDoesNotPromoteDestroyingAssetWithOldResourceId()
    {
        await using var context = Context();
        var runtime = new TeamLabRuntime { Generation = 2, Status = TeamLabRuntimeStatus.Running };
        context.TeamLabRuntimes.Add(runtime);
        context.TeamLabRuntimeAssets.Add(new TeamLabRuntimeAsset
        {
            Runtime = runtime, Generation = 2, Kind = TeamLabResourceKind.Vm,
            TopologyKey = "old-vm", Name = "Old VM", Status = TeamLabRuntimeStatus.Destroying,
            WorkerNodeId = Guid.NewGuid(), RuntimeResourceId = "leftover-resource"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var detail = await new TeamLabRuntimeProjectionService(context, new EfImageRuntimeAccessQuery(context))
            .GetAsync(runtime.PublicId, CancellationToken.None);

        var asset = Assert.Single(detail.Assets);
        Assert.Equal(TeamLabRuntimeStatus.Destroying, asset.Status);
        Assert.Equal("currently-unavailable", Assert.Single(asset.Capabilities!, item =>
            item.Kind == "console").Status);
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

        var template = new ImageRuntimeAccessSummary(42, RawDigest, OSType.Linux, null, false);
        Assert.Equal(("linux", "template-current"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        Assert.Equal("unconfigured", TeamLabRuntimeAssetFacts.Capabilities(asset,
            TeamLabRuntimeStatus.Running, 4, "linux", null)
            .Single(item => item.Kind == "ssh").Status);
        template = template with { OperatingSystem = OSType.Windows };
        Assert.Equal(("windows", "template-current"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        template = template with { ImageHash = "changed-image" };
        Assert.Equal(("unknown", "unknown"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        Assert.Equal(("unknown", "unknown"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, null));
    }

    [Fact]
    public void InvalidOrDifferentDigestCannotBorrowPlanOperatingSystemOrInterfaces()
    {
        var asset = Asset();
        var executed = new TeamLabRuntimeAssetFacts.ExecutedAsset(
            Spec(TeamLabGuestOperatingSystem.Windows, "10.66.0.15", "172.22.1.15"), true);
        var template = new ImageRuntimeAccessSummary(42, RawDigest, OSType.Windows, "rdp", true);
        Assert.Equal(("windows", "execution-plan"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        Assert.Equal(2, TeamLabRuntimeAssetFacts.Interfaces(asset, executed).Count);

        asset.ImageDigest = new string('b', 64);
        Assert.Equal(("unknown", "unknown"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        Assert.Empty(TeamLabRuntimeAssetFacts.Interfaces(asset, executed));
        asset.ImageDigest = "sha256:not-a-digest";
        Assert.Equal(("unknown", "unknown"),
            TeamLabRuntimeAssetFacts.OperatingSystem(asset, executed, template));
        Assert.Empty(TeamLabRuntimeAssetFacts.Interfaces(asset, executed));
    }

    [Fact]
    public void DockerPlanWithCanonicalDigestKeepsItsAssignedInterface()
    {
        var asset = Asset();
        asset.Kind = TeamLabResourceKind.Docker;
        asset.SourceTemplateId = 495;
        asset.InterfaceSummaryJson = JsonSerializer.Serialize(new[]
        {
            new { Key = "eth0", NetworkKey = "edge", IpAddress = "10.66.0.15",
                PrefixLength = 24, MacAddress = "02:42:00:00:00:01", Primary = true }
        });
        var spec = Spec(TeamLabGuestOperatingSystem.Linux, "10.66.0.15", "172.22.1.15") with
        {
            Kind = "docker", TemplateId = 495,
            NetworkAttachments = [new TeamLabAssetNetworkAttachmentV2(
                "edge", "web:eth0", "eth0", "10.66.0.15", "10.66.0.1", true,
                InterfaceKey: "eth0", PrefixLength: 24)]
        };
        var executed = new TeamLabRuntimeAssetFacts.ExecutedAsset(spec, false);

        var iface = Assert.Single(TeamLabRuntimeAssetFacts.Interfaces(asset, executed));
        Assert.Equal("10.66.0.15", iface.Assigned?.IpAddress);
        Assert.Null(iface.Observed);
        Assert.Empty(TeamLabRuntimeAssetFacts.Interfaces(asset, executed with
        {
            Spec = spec with { Kind = "vm" }
        }));
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

        var configured = new ImageRuntimeAccessSummary(42, Digest, OSType.Windows, "rdp", true);
        var capabilities = TeamLabRuntimeAssetFacts.Capabilities(asset, TeamLabRuntimeStatus.Running, 4,
            os.Value, configured);
        Assert.Equal("configured-unverified", capabilities.Single(item => item.Kind == "rdp").Status);
        Assert.Equal(42, capabilities.Single(item => item.Kind == "rdp").SettingsTemplateId);
        Assert.Equal("unsupported", capabilities.Single(item => item.Kind == "files").Status);
        Assert.Equal("unsupported", capabilities.Single(item => item.Kind == "ssh").Status);

        asset.Status = TeamLabRuntimeStatus.Deploying;
        Assert.True(TeamLabRuntimeAssetFacts.IsRunning(asset, TeamLabRuntimeStatus.Running, 4));
        Assert.Equal("configured-unverified", TeamLabRuntimeAssetFacts.Capabilities(asset,
            TeamLabRuntimeStatus.Running, 4, os.Value, configured)
            .Single(item => item.Kind == "rdp").Status);
        asset.Status = TeamLabRuntimeStatus.Failed;
        Assert.False(TeamLabRuntimeAssetFacts.IsRunning(asset, TeamLabRuntimeStatus.Running, 4));
        asset.Status = TeamLabRuntimeStatus.Running;
        asset.Generation = 3;
        Assert.False(TeamLabRuntimeAssetFacts.IsRunning(asset, TeamLabRuntimeStatus.Running, 4));
        asset.Generation = 4;
        asset.Status = TeamLabRuntimeStatus.Destroying;
        Assert.False(TeamLabRuntimeAssetFacts.IsRunning(asset, TeamLabRuntimeStatus.Running, 4));
        asset.Status = TeamLabRuntimeStatus.CleanupPending;
        Assert.False(TeamLabRuntimeAssetFacts.IsRunning(asset, TeamLabRuntimeStatus.Running, 4));
    }

    private static TeamLabRuntimeAsset Asset() => new()
    {
        RuntimeId = 8, Generation = 4, Kind = TeamLabResourceKind.Vm,
        TopologyKey = "web", SourceTemplateId = 42, ImageDigest = RawDigest,
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
