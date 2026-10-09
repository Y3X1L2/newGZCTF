using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class ImageTemplateDeletionQueryPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("image_deletion_query").WithUsername("postgres").WithPassword("postgres")
        .WithCleanUp(true).Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task NormalDeletion_WithoutDistributionRecords_CompletesActualDistributionCleanup()
    {
        await using var context = Context();
        var template = Template();
        context.Add(template);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var deletion = new ImageTemplateDeletionService(
            new EfImageTemplateCatalog(context, new DistributionCleaner(Service(context, agent))),
            new ImageTemplateReferenceService([]));

        var result = await deletion.DeleteAsync(template.Id, new ActorContext(Guid.NewGuid(), Role.Admin), default);

        Assert.Equal(ImageTemplateDeleteStatus.Deleted, result.Status);
        Assert.Empty(await context.ImageTemplates.AsNoTracking().ToArrayAsync());
        Assert.Empty(agent.DeletedNodes);
    }

    [Fact]
    public async Task Cleanup_WithIdleDistributionRecords_ClaimsAndCleansBothNodeCaches()
    {
        await using var context = Context();
        var template = Template();
        var records = new[] { Record(template), Record(template) };
        context.AddRange(records);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();

        await Service(context, agent).CleanupTemplateForDeletionAsync(template.Id, default);

        Assert.Equal(records.Select(item => item.WorkerNodeId).Order(), agent.DeletedNodes.Order());
        Assert.Empty(await context.ImageDistributionReferences.AsNoTracking().ToArrayAsync());
        var cleaned = await context.ImageDistributionRecords.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, cleaned.Length);
        Assert.All(cleaned, item =>
        {
            Assert.Equal(ImageDistributionOperation.Cleanup, item.Operation);
            Assert.Equal(ImageDistributionStatus.CleanupPending, item.Status);
            Assert.Null(item.ClaimOwner);
            Assert.Null(item.ClaimExpiresAt);
            Assert.Null(item.ErrorMessage);
        });
        Assert.Equal(ImageStatus.Ready, (await context.ImageTemplates.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Cleanup_WithActiveClaim_PreservesReferencesWithoutCallingAgent()
    {
        await using var context = Context();
        var record = Record(Template());
        record.Status = ImageDistributionStatus.Pulling;
        record.ClaimOwner = "active-transfer";
        record.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        context.Add(record);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(context, agent).CleanupTemplateForDeletionAsync(record.ImageTemplateId, default));

        Assert.Contains("is processing its distribution or cleanup", error.Message);
        await context.Entry(record).ReloadAsync();
        Assert.Equal(ImageDistributionStatus.Pulling, record.Status);
        Assert.Equal("active-transfer", record.ClaimOwner);
        Assert.Single(await context.ImageDistributionReferences.AsNoTracking().ToArrayAsync());
        Assert.Empty(agent.DeletedNodes);
    }

    private AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    private static ImageDistributionService Service(AppDbContext context, AgentClient agent) => new(
        context, agent, null!, null!, null!, new ImageDistributionCoordinator(), new DeploymentExecutionContextAccessor(),
        new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance),
        NullLogger<ImageDistributionService>.Instance);

    private static ImageTemplate Template() => new()
    {
        Name = "deletion-query-fixture", ImageType = ImageType.Qcow2, Status = ImageStatus.Ready,
        ImageHash = new string('a', 64), FileSize = 1024
    };

    private static ImageDistributionRecord Record(ImageTemplate template) => new()
    {
        ImageTemplate = template, ImageType = ImageType.Qcow2, ImageHash = template.ImageHash!,
        Status = ImageDistributionStatus.Ready,
        WorkerNode = new()
        {
            Name = "cleanup-" + Guid.NewGuid().ToString("N"), HostAddress = "127.0.0.1", AuthToken = Guid.NewGuid().ToString("N"),
            Status = NodeStatus.Online, Capabilities = NodeCapability.Kvm, IsSchedulable = true, MaxVms = 4
        },
        References = [new() { Kind = ImageDistributionReferenceKind.TeamLabTemplatePreparation }]
    };

    // The SQL/catalog/cleanup service are real. Only the remote hardware boundary is substituted.
    private sealed class DistributionCleaner(ImageDistributionService distribution) : IImageTemplateArtifactCleaner
    {
        public Task CleanupAsync(ImageTemplate template, CancellationToken token) =>
            distribution.CleanupTemplateForDeletionAsync(template.Id, token);
    }

    private sealed class RecordingAgentClient() : AgentClient(
        null!, null!, new ConfigurationBuilder().Build(), NullLogger<AgentClient>.Instance)
    {
        public List<Guid> DeletedNodes { get; } = [];

        public override Task<AgentImageCacheCleanupResult> DeleteVmImageWithInventoryAsync(
            Guid nodeId, int templateId, string hash, CancellationToken token)
        {
            DeletedNodes.Add(nodeId);
            return Task.FromResult(new AgentImageCacheCleanupResult([new("vm", hash, false)]));
        }
    }
}
