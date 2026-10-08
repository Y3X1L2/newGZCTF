using System.Data.Common;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class ImagePreparationReclamationPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("image_preparation_reclamation").WithUsername("postgres").WithPassword("postgres")
        .WithCleanUp(true).Build();

    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task ConditionalRelease_PreservesPrewarmRenewedBetweenCandidateReadAndDistributionLock()
    {
        await using var renewalContext = Context();
        await renewalContext.Database.EnsureCreatedAsync();
        var node = new WorkerNode
        {
            Name = "fixture", HostAddress = "127.0.0.1", AuthToken = Guid.NewGuid().ToString("N"),
            Status = NodeStatus.Online, Capabilities = NodeCapability.Kvm, MaxVms = 10,
            IsSchedulable = true, LastHeartbeat = DateTimeOffset.UtcNow
        };
        var template = new ImageTemplate
        {
            Name = "fixture", ImageType = ImageType.Qcow2, Status = ImageStatus.Ready,
            ImageHash = new string('a', 64), FileSize = 1024
        };
        var release = new TeamLabTopologyRelease
        {
            Topology = new TeamLabTopology { Name = "fixture" }, CanonicalJson = "{}"
        };
        var destroyedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var runtime = new TeamLabRuntime
        {
            TopologyReleaseId = release.Id, Status = TeamLabRuntimeStatus.Destroyed, UpdatedAt = destroyedAt
        };
        var reference = new ImageDistributionReference
        {
            Kind = ImageDistributionReferenceKind.TeamLabRelease, ResourcePublicId = release.Id,
            CreatedAt = destroyedAt.AddMinutes(-1)
        };
        var record = new ImageDistributionRecord
        {
            ImageTemplate = template, WorkerNode = node, ImageType = ImageType.Qcow2,
            ImageHash = template.ImageHash, Status = ImageDistributionStatus.Ready, References = [reference]
        };
        renewalContext.AddRange(release, runtime, record);
        await renewalContext.SaveChangesAsync();
        var renewedAt = destroyedAt.AddMinutes(1);
        var interceptor = new RenewBeforeLockInterceptor(() => renewalContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ImageDistributionReferences\" SET \"CreatedAt\" = {renewedAt} WHERE \"Id\" = {reference.Id}"));
        await using var context = Context(interceptor);
        // Keep an old timestamp in the identity map, as a long-lived request can do.
        await context.ImageDistributionReferences.SingleAsync(item => item.Id == reference.Id);
        interceptor.Enabled = true;
        var service = Service(context);

        await service.ReleaseTeamLabReleaseReferencesBeforeAsync(release.Id, destroyedAt, default);

        Assert.True(interceptor.Triggered);
        Assert.Single(await context.ImageDistributionReferences.AsNoTracking().ToArrayAsync());
        Assert.Equal(ImageDistributionStatus.Ready,
            (await context.ImageDistributionRecords.AsNoTracking().SingleAsync()).Status);
        await service.ReconcileReferencesAsync(default);
        Assert.Single(await context.ImageDistributionReferences.AsNoTracking().ToArrayAsync());

        // A genuine missed destruction tail is recovered; only node demand is removed.
        var oldTimestamp = destroyedAt.AddMinutes(-1);
        await renewalContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ImageDistributionReferences\" SET \"CreatedAt\" = {oldTimestamp} WHERE \"Id\" = {reference.Id}");
        await service.ReconcileReferencesAsync(default);
        Assert.Empty(await context.ImageDistributionReferences.AsNoTracking().ToArrayAsync());
        Assert.Equal(ImageDistributionStatus.CleanupPending,
            (await context.ImageDistributionRecords.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(ImageStatus.Ready, (await context.ImageTemplates.AsNoTracking().SingleAsync()).Status);
    }

    private AppDbContext Context(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString());
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    private static ImageDistributionService Service(AppDbContext context) => new(
        context, null!, null!, null!, null!, new ImageDistributionCoordinator(), new DeploymentExecutionContextAccessor(),
        new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance),
        NullLogger<ImageDistributionService>.Instance);

    private sealed class RenewBeforeLockInterceptor(Func<Task> renew) : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public bool Triggered { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && !Triggered && command.CommandText.Contains("SELECT DISTINCT", StringComparison.Ordinal) &&
                command.CommandText.Contains("ImageDistributionReferences", StringComparison.Ordinal))
            {
                Triggered = true;
                await renew();
            }
            return result;
        }
    }
}
