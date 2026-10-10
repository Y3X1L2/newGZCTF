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
    private readonly TeamLabRuntimeOperationPayloadProtector protector = new(new EphemeralDataProtectionProvider());

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
        await new TeamLabReleaseService(context, new TeamLabTopologyValidator()).ArchiveAsync(fixture.Release.Id, default);
        Assert.Equal(ImageTemplateDeleteStatus.InUse,
            (await deletion.DeleteAsync(fixture.Template.Id, Admin(fixture.Owner), default)).Status);

        var topologies = new TeamLabTopologyApplicationService(context, new TeamLabTopologyValidator(),
            new TeamLabReleaseService(context, new TeamLabTopologyValidator()), new TeamLabControlScopeService(context),
            new NodeCapacitySnapshotService(context));
        await topologies.UpdateDraftAsync(fixture.Topology.PublicId,
            new(fixture.Topology.Revision, "retired draft", [], [], [], SchemaVersion: 2), fixture.Owner, true, default);
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
        var history = await new TeamLabRuntimeProjectionService(context, new EfImageRuntimeAccessQuery(context))
            .GetAsync(runtime.PublicId, default);
        Assert.Equal(TeamLabRuntimeStatus.Destroyed, history.Status);
        Assert.Equal(fixture.Release.Id, history.ReleaseId);
        Assert.Equal("retirement fixture", history.TopologyName);
        var audit = await topologies.GetReleaseAsync(fixture.Topology.PublicId, fixture.Release.Id, fixture.Owner, true, default);
        Assert.True(audit.Archived);
    }

    [Fact]
    public async Task CanonicalDelete_ProtectsActiveResetAndFailedRolloutThenAllowsTerminalRetirement()
    {
        await using var context = Context();
        var fixture = await SeedAsync(context);
        await new TeamLabReleaseService(context, new TeamLabTopologyValidator()).ArchiveAsync(fixture.Release.Id, default);
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

    [Fact]
    public async Task ArchiveWinsAfterInitialCreateRead_StalePlannerCannotCreateFromRetiredBytes()
    {
        await using var seed = Context();
        var fixture = await SeedAsync(seed);
        var barrier = new ReleaseReadBarrier();
        await using var createContext = Context(barrier);
        var create = Planner(createContext).CreateAsync(new(fixture.Release.Id, null, null, null), fixture.Owner,
            "fixture-create", null, null, default);
        await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var archiveContext = Context();
        try
        {
            await new TeamLabReleaseService(archiveContext, new TeamLabTopologyValidator()).ArchiveAsync(fixture.Release.Id, default);
        }
        finally { barrier.Proceed.TrySetResult(); }

        var exception = await Assert.ThrowsAsync<TeamLabApiContractException>(() => create);
        Assert.Equal("release_archived", exception.Code);
        Assert.Empty(await archiveContext.TeamLabRuntimes.Where(item => item.TopologyReleaseId == fixture.Release.Id).ToArrayAsync());
    }

    [Fact]
    public async Task ResetPermit_BlocksArchiveAfterReadAcrossReleasesAndNormalReplanSucceeds()
    {
        await using var context = Context();
        var source = await SeedAsync(context);
        var target = new TeamLabTopologyRelease
        {
            TopologyId = source.Topology.Id, Version = 2, SourceRevision = 2, SchemaVersion = 2,
            CanonicalJson = source.Release.CanonicalJson, ContentHash = source.Release.ContentHash
        };
        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = source.Release.Id, CreatedById = source.Owner, Status = TeamLabRuntimeStatus.Running,
            Assets = [new() { TopologyKey = "vm", SourceTemplateId = source.Template.Id, Status = TeamLabRuntimeStatus.Running }]
        };
        context.AddRange(target, runtime);
        await context.SaveChangesAsync();
        var queue = new RecordingQueue(context);
        var orchestrator = new TeamLabRuntimeOrchestrator(context, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, new TeamLabRuntimeLifecycleGuard(context), protector, queue, null!, Recorder(context),
            NullLogger<TeamLabRuntimeOrchestrator>.Instance);
        await orchestrator.ResetAndEnqueueAsync(runtime.PublicId, new(null, target.Id), null, default);
        Assert.Equal(1, queue.Notifications);
        var ticket = await context.DeploymentQueueTickets.SingleAsync(item => item.TeamLabRuntimeId == runtime.Id);
        ticket.Status = DeploymentQueueTicketStatus.Running;
        await context.SaveChangesAsync();

        var checkedRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalResourcePresent = true;
        var reset = Task.Run(async () =>
        {
            await using var resetContext = Context();
            await TeamLabReleaseLifecycle.RequireStartableAsync(resetContext, target.Id, default);
            checkedRelease.TrySetResult();
            await proceed.Task;
            var loaded = await resetContext.TeamLabRuntimes.Include(item => item.Assets)
                .SingleAsync(item => item.Id == runtime.Id);
            // Hardware cleanup is an external port; converge its successful database terminal
            // state with the actual cleanup method, then invoke the actual PostgreSQL planner.
            await TeamLabRuntimeCleanupService.FinalizeGenerationAsync(resetContext, loaded, loaded.Generation, true, default);
            originalResourcePresent = false;
            return await Planner(resetContext).ResetAsync(runtime.PublicId, null, target.Id, null, default);
        });
        await checkedRelease.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var archiveContext = Context();
        var archives = new TeamLabReleaseService(archiveContext, new TeamLabTopologyValidator(), operationPayloads: protector);
        try
        {
            foreach (var releaseId in new[] { source.Release.Id, target.Id })
            {
                var busy = await Assert.ThrowsAsync<TeamLabApiContractException>(() => archives.ArchiveAsync(releaseId, default));
                Assert.Equal("release_reset_in_progress", busy.Code);
                Assert.True(originalResourcePresent);
            }
            // Cancellation does not erase a still-owned execution lease.
            ticket.Status = DeploymentQueueTicketStatus.Cancelled;
            ticket.ClaimOwner = "fixture-reset";
            ticket.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
            await context.SaveChangesAsync();
            Assert.Equal("release_reset_in_progress", (await Assert.ThrowsAsync<TeamLabApiContractException>(
                () => archives.ArchiveAsync(target.Id, default))).Code);
            ticket.Status = DeploymentQueueTicketStatus.Running;
            await context.SaveChangesAsync();
        }
        finally { proceed.TrySetResult(); }

        await reset.WaitAsync(TimeSpan.FromSeconds(20));
        var rebuilt = await archiveContext.TeamLabRuntimes.AsNoTracking().SingleAsync(item => item.Id == runtime.Id);
        Assert.False(originalResourcePresent);
        Assert.Equal(2, rebuilt.Generation);
        Assert.Equal(target.Id, rebuilt.TopologyReleaseId);
        Assert.False((await archiveContext.TeamLabTopologyReleases.AsNoTracking().SingleAsync(item => item.Id == target.Id)).IsArchived);
        ticket.Status = DeploymentQueueTicketStatus.Succeeded;
        ticket.ClaimOwner = null;
        ticket.ClaimExpiresAt = null;
        await context.SaveChangesAsync();
        await archives.ArchiveAsync(target.Id, default);
        Assert.True((await archiveContext.TeamLabTopologyReleases.AsNoTracking().SingleAsync(item => item.Id == target.Id)).IsArchived);
        Assert.False((await References(archiveContext).CanDeleteAsync(source.Template.Id, default)).Allowed);
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

    private static TeamLabEventRecorder Recorder(AppDbContext context) => new(context,
        new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance), new OperationalCorrelation());

    private static TeamLabRuntimePlanner Planner(AppDbContext context) => new(context,
        new TeamLabRuntimeOverlayService(new EphemeralDataProtectionProvider()), Recorder(context), null!,
        Options.Create(new TeamLabNetworkConfig()), new GZCTF.Modules.Runtime.Application.TeamLabRecordReferenceQuery(context));

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

    private sealed class RecordingQueue(AppDbContext context) : ITeamLabRuntimeQueue
    {
        public int Notifications { get; private set; }
        public Task<TeamLabQueueTicketResult> EnqueueAsync(TeamLabQueueRequest request, CancellationToken token) =>
            throw new InvalidOperationException("PostgreSQL reset must publish its ticket in the current transaction.");
        public async Task<TeamLabQueueTicketResult> EnqueueInCurrentTransactionAsync(TeamLabQueueRequest request, CancellationToken token)
        {
            Assert.NotNull(context.Database.CurrentTransaction);
            var ticket = new DeploymentQueueTicket
            {
                Kind = DeploymentQueueKind.TeamLabRuntime, Operation = request.Operation,
                TeamLabRuntimeId = request.RuntimeId, Generation = request.Generation, ProtectedPayload = request.ProtectedPayload,
                Status = DeploymentQueueTicketStatus.Pending
            };
            context.Add(ticket);
            await context.SaveChangesAsync(token);
            return new(ticket.Id);
        }
        public Task NotifyAsync(Guid ticketId, CancellationToken token) { Notifications++; return Task.CompletedTask; }
    }

    private sealed class ReleaseReadBarrier : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool triggered;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken token = default)
        {
            if (!triggered && command.CommandText.Contains("FROM \"TeamLabTopologyReleases\"", StringComparison.Ordinal))
            {
                triggered = true;
                Reached.TrySetResult();
                await Proceed.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            }
            return result;
        }
    }
}
