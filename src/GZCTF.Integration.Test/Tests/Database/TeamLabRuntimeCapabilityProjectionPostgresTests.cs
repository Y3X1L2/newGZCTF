using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabRuntimeCapabilityProjectionPostgresTests : IAsyncLifetime
{
    private const string ImageDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("teamlab_capability_projection")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task ContentBatchAndRuntimeProjectionTranslateOnPostgres()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.EnsureCreatedAsync();

        var template = new ImageTemplate
        {
            Id = 42, Name = "windows-fixture", ImageHash = ImageDigest,
            OSType = OSType.Windows, ImageType = ImageType.Qcow2
        };
        var configuration = new ImageTemplateRemoteAccess
        {
            ImageTemplateId = template.Id, Enabled = true, Protocol = TeamLabRemoteProtocol.Rdp,
            Port = 3389, Username = "  ", ProtectedSecret = "  "
        };
        var topology = new TeamLabTopology { Name = "Editable scene" };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology, Version = 2,
            CanonicalJson = "{\"name\":\"Frozen scene\"}"
        };
        var node = new WorkerNode
        {
            Name = "Local fixture node", HostAddress = "127.0.0.1",
            AuthToken = Guid.NewGuid().ToString("N")
        };
        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = release.Id, Generation = 2,
            Status = TeamLabRuntimeStatus.Running
        };
        var shard = new TeamLabRuntimeShard { Runtime = runtime, Generation = 2, WorkerNode = node };
        var asset = new TeamLabRuntimeAsset
        {
            Runtime = runtime, Shard = shard, Generation = 2,
            Kind = TeamLabResourceKind.Vm, TopologyKey = "windows",
            Name = "Windows VM", SourceTemplateId = template.Id,
            ImageDigest = ImageDigest, Status = TeamLabRuntimeStatus.Running,
            WorkerNode = node, RuntimeResourceId = "fixture-vm",
            NativeIdentity = Guid.NewGuid().ToString("D"), IpAddress = "10.10.0.10"
        };
        db.ImageTemplates.Add(template);
        db.ImageTemplateRemoteAccesses.Add(configuration);
        db.TeamLabTopologyReleases.Add(release);
        db.TeamLabRuntimeAssets.Add(asset);
        await db.SaveChangesAsync();

        var planAsset = new TeamLabAssetExecutionSpecV2(
            asset.TopologyKey, "vm", "fixture-vm", "sha256:" + ImageDigest, "fixture-vm",
            template.Id, 2, 2048, [], [], OperatingSystem: TeamLabGuestOperatingSystem.Windows);
        var plan = new TeamLabExecutionPlanV2(runtime.Id, runtime.PublicId, runtime.Generation,
            "fixture-shard", "digest", "network-digest", true, [], [planAsset], []);
        db.TeamLabExecutionPlanSnapshots.Add(new TeamLabExecutionPlanSnapshot
        {
            RuntimeId = runtime.Id, Generation = runtime.Generation, ShardId = shard.Id,
            PlanJson = JsonSerializer.Serialize(plan)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var images = new EfImageRuntimeAccessQuery(db);
        Assert.False((await images.GetBatchAsync([template.Id], default))[template.Id].RemoteConfigured);
        var persisted = await db.ImageTemplateRemoteAccesses.SingleAsync(item => item.ImageTemplateId == template.Id);
        persisted.Username = "operator";
        persisted.ProtectedSecret = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        Assert.True((await images.GetBatchAsync([template.Id], default))[template.Id].RemoteConfigured);
        var detail = await new TeamLabRuntimeProjectionService(db, images)
            .GetAsync(runtime.PublicId, default);
        Assert.Equal(topology.PublicId, detail.TopologyId);
        Assert.Equal("Frozen scene", detail.TopologyName);
        var current = Assert.Single(detail.Assets);
        Assert.Equal("windows", current.OperatingSystem);
        Assert.Equal("execution-plan", current.OperatingSystemSource);
        Assert.Equal("configured-unverified", Assert.Single(current.Capabilities!, item =>
            item.Kind == "rdp").Status);
        Assert.Empty(await db.TeamLabRemoteSessions.ToArrayAsync());
    }
}
