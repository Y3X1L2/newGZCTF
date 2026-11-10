using System.Data.Common;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class ImageRetirementPostgresTests : IAsyncLifetime
{
    private const string Digest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("image_retirement").WithUsername("postgres").WithPassword("postgres")
        .WithCleanUp(true).Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task NormalDraftClearArchiveAndDelete_PreservesFrozenAuditAndHistoricalRuntime()
    {
        await using var seed = Context();
        var fixture = await SeedAsync(seed, draft: true);
        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = fixture.Release.Id, CreatedById = fixture.Owner,
            Status = TeamLabRuntimeStatus.Destroyed, Assets = [new()
            {
                TopologyKey = "vm", Name = "frozen VM", SourceTemplateId = fixture.Template.Id,
                ImageDigest = Digest, Status = TeamLabRuntimeStatus.Destroyed
            }]
        };
        seed.Add(runtime);
        await seed.SaveChangesAsync();
        var frozen = await seed.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == fixture.Release.Id).Select(item => item.CanonicalJson).SingleAsync();
        var contentHash = fixture.Release.ContentHash;

        await using var context = Context();
        var cleaner = new RecordingCleaner();
        var deletion = Deletion(context, cleaner);
        (await context.TeamLabTopologyReleases.SingleAsync(item => item.Id == fixture.Release.Id)).IsArchived = true;
        await context.SaveChangesAsync();
        Assert.Equal(ImageTemplateDeleteStatus.InUse,
            (await deletion.DeleteAsync(fixture.Template.Id, Admin(fixture.Owner), default)).Status);

        var draft = await context.TeamLabTopologies.Include(item => item.Assets).SingleAsync(item => item.Id == fixture.Topology.Id);
        draft.Assets.Clear();
        await context.SaveChangesAsync();
        Assert.Equal(ImageTemplateDeleteStatus.Deleted,
            (await deletion.DeleteAsync(fixture.Template.Id, Admin(fixture.Owner), default)).Status);
        Assert.Equal(1, cleaner.Calls);
        Assert.Empty(await context.ImageTemplates.Where(item => item.Id == fixture.Template.Id).ToArrayAsync());

        var release = await context.TeamLabTopologyReleases.AsNoTracking().SingleAsync(item => item.Id == fixture.Release.Id);
        Assert.True(release.IsArchived);
        Assert.Equal(frozen, release.CanonicalJson);
        Assert.Equal(contentHash, release.ContentHash);
        var asset = await context.TeamLabRuntimeAssets.AsNoTracking().SingleAsync(item => item.RuntimeId == runtime.Id);
        Assert.Equal(fixture.Template.Id, asset.SourceTemplateId);
        Assert.Equal(Digest, asset.ImageDigest);
        Assert.DoesNotContain(context.Model.FindEntityType(typeof(TeamLabRuntimeAsset))!.GetForeignKeys(),
            key => key.Properties.Any(property => property.Name == nameof(TeamLabRuntimeAsset.SourceTemplateId)));

    }

    [Fact]
    public async Task CanonicalDelete_ProtectsActiveResetAndFailedRolloutThenAllowsTerminalRetirement()
    {
        await using var context = Context();
        var fixture = await SeedAsync(context);
        (await context.TeamLabTopologyReleases.SingleAsync(item => item.Id == fixture.Release.Id)).IsArchived = true;
        await context.SaveChangesAsync();
        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = fixture.Release.Id, Status = TeamLabRuntimeStatus.Stopped,
            Assets = [new() { SourceTemplateId = fixture.Template.Id, Status = TeamLabRuntimeStatus.Stopped }]
        };
        context.Add(runtime);
        await context.SaveChangesAsync();
        var cleaner = new RecordingCleaner();
        var deletion = Deletion(context, cleaner);
        Assert.Equal(ImageTemplateDeleteStatus.InUse,
            (await deletion.DeleteAsync(fixture.Template.Id, Admin(fixture.Owner), default)).Status);

        runtime.Status = runtime.Assets.Single().Status = TeamLabRuntimeStatus.Destroyed;
        var ticket = new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.TeamLabRuntime, TeamLabRuntimeId = runtime.Id,
            Operation = RuntimeOperationKind.Reset, Status = DeploymentQueueTicketStatus.Running
        };
        context.Add(ticket);
        await context.SaveChangesAsync();
        Assert.Equal(ImageTemplateDeleteStatus.InUse,
            (await deletion.DeleteAsync(fixture.Template.Id, Admin(fixture.Owner), default)).Status);

        ticket.Status = DeploymentQueueTicketStatus.Failed;
        var rollout = new TeamLabRollout { ReleaseId = fixture.Release.Id, Status = TeamLabRolloutStatus.Failed };
        context.Add(rollout);
        await context.SaveChangesAsync();
        var blocked = await deletion.DeleteAsync(fixture.Template.Id, Admin(fixture.Owner), default);
        Assert.Equal(ImageTemplateDeleteStatus.InUse, blocked.Status);
        Assert.Contains(blocked.References, item => item.ResourceType == "rollout");
        rollout.Status = TeamLabRolloutStatus.Archived;
        await context.SaveChangesAsync();
        Assert.Equal(ImageTemplateDeleteStatus.Deleted,
            (await deletion.DeleteAsync(fixture.Template.Id, Admin(fixture.Owner), default)).Status);
        Assert.Equal(1, cleaner.Calls);
    }

    private AppDbContext Context(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString());
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    private static ActorContext Admin(Guid owner) => new(owner, Role.Admin);
    private static ImageTemplateReferenceService References(AppDbContext context) => new([new TeamLabImageTemplateReferenceProvider(context)]);
    private static ImageTemplateDeletionService Deletion(AppDbContext context, RecordingCleaner cleaner) =>
        new(new EfImageTemplateCatalog(context, cleaner), References(context));

    private static async Task<Fixture> SeedAsync(AppDbContext context, bool draft = false)
    {
        var owner = Guid.NewGuid();
        context.Users.Add(new UserInfo
        {
            Id = owner, UserName = "retire-" + owner.ToString("N")[..8],
            NormalizedUserName = "RETIRE-" + owner.ToString("N")[..8].ToUpperInvariant(),
            Role = Role.Admin, RegisterTimeUtc = DateTimeOffset.UtcNow
        });
        var template = new ImageTemplate
        {
            Name = "fixture-" + Guid.NewGuid(), ImageType = ImageType.Qcow2, OSType = OSType.Linux,
            Status = ImageStatus.Ready, ImageHash = Digest, FileSize = 1024, VmNetworkMode = VmNetworkMode.Dhcp
        };
        context.Add(template);
        await context.SaveChangesAsync();
        var topology = new TeamLabTopology { Name = "retirement fixture", OwnerUserId = owner, SchemaVersion = 2, Revision = 1 };
        var definition = new TeamLabTopologyDefinitionModel("retirement fixture",
            [new("entry", "entry", new("10.210.0.0/16", 24), true)],
            [new("vm", "frozen VM", TeamLabAssetKind.Vm, template.Id, new(1, 256, 0), [new("eth0", "entry", 10, true)])], []);
        var canonical = TeamLabReleaseCodec.Encode(2, definition, new Dictionary<string, string> { ["vm"] = Digest });
        var release = new TeamLabTopologyRelease
        {
            Topology = topology, Version = 1, SourceRevision = 1, SchemaVersion = 2, CanonicalJson = canonical,
            ContentHash = TeamLabReleaseCodec.ComputeContentHash(2, canonical)
        };
        context.Add(release);
        if (draft) topology.Assets.Add(new TeamLabTopologyAsset { Key = "vm", Name = "draft VM", ImageTemplateId = template.Id });
        await context.SaveChangesAsync();
        return new(topology, release, template, owner);
    }

    private sealed record Fixture(TeamLabTopology Topology, TeamLabTopologyRelease Release, ImageTemplate Template, Guid Owner);

    private sealed class RecordingCleaner : IImageTemplateArtifactCleaner
    {
        public int Calls { get; private set; }
        public Task CleanupAsync(ImageTemplate template, CancellationToken token)
        {
            Assert.Equal(ImageStatus.Deleting, template.Status);
            Calls++;
            return Task.CompletedTask;
        }
    }
}
