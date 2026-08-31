using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

/// <summary>
/// Covers the external-API closure additions: topology clone, release
/// archive lifecycle, capture listing and structured flow port filtering.
/// </summary>
public sealed class TeamLabExternalApiClosureTests
{
    [Fact]
    public async Task CloneAsync_CopiesDefinitionIntoAFreshDraft()
    {
        using var context = CreateContext();
        context.ImageTemplates.Add(new GZCTF.Models.Data.ImageTemplate
        {
            Id = 1,
            Name = "base",
            ImageType = GZCTF.Models.Data.ImageType.Docker,
            Status = GZCTF.Models.Data.ImageStatus.Ready,
            ImageHash = new string('d', 64)
        });
        await context.SaveChangesAsync();
        var (service, _) = CreateTopologyService(context);
        var created = await service.CreateDraftAsync(
            new CreateTeamLabTopologyModel("源场景", [Network()], [Asset()], []), Actor(), CancellationToken.None);

        var clone = await service.CloneAsync(created.Id, Actor(), includeAll: true, CancellationToken.None);

        Assert.NotEqual(created.Id, clone.Id);
        Assert.Equal("源场景 (副本)", clone.Definition.Name);
        Assert.Equal(created.Definition.Assets[0].Key, clone.Definition.Assets[0].Key);
        Assert.Equal(created.ControlScopeId, clone.ControlScopeId);
        Assert.Equal(1, clone.Revision);

        var missing = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.CloneAsync(Guid.NewGuid(), Actor(), true, CancellationToken.None));
        Assert.Equal("topology_not_found", missing.Code);
    }

    [Fact]
    public async Task ReleaseArchive_IsIdempotentAndProjectsTheFlag()
    {
        using var context = CreateContext();
        var release = new TeamLabTopologyRelease { TopologyId = 1, Version = 1, CanonicalJson = "{}" };
        context.TeamLabTopologyReleases.Add(release);
        await context.SaveChangesAsync();
        var (_, releases) = CreateTopologyService(context);

        await releases.ArchiveAsync(release.Id, CancellationToken.None);
        await releases.ArchiveAsync(release.Id, CancellationToken.None);

        Assert.True(release.IsArchived);
        Assert.NotNull(release.ArchivedAt);
        Assert.True(TeamLabReleaseService.ToModel(release, Guid.NewGuid()).Archived);
        var missing = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => releases.ArchiveAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("release_not_found", missing.Code);
    }

    [Fact]
    public async Task ListCapturesAsync_PagesNewestFirst()
    {
        using var context = CreateContext();
        var runtime = new TeamLabRuntime();
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        var older = CaptureJob(runtime, "scope-a");
        var newer = CaptureJob(runtime, "scope-b");
        context.TeamLabTrafficCaptureJobs.AddRange(older, newer);
        await context.SaveChangesAsync();
        var service = CreateTrafficService(context);

        var firstPage = await service.ListCapturesAsync(runtime.PublicId, null, 1, CancellationToken.None);
        var secondPage = await service.ListCapturesAsync(runtime.PublicId, firstPage.Next, 50, CancellationToken.None);

        Assert.Single(firstPage.Items);
        Assert.Equal(newer.PublicId, firstPage.Items[0].Id);
        Assert.NotNull(firstPage.Next);
        Assert.Single(secondPage.Items);
        Assert.Equal(older.PublicId, secondPage.Items[0].Id);
        Assert.Null(secondPage.Next);
    }

    [Fact]
    public async Task GetFlowsAsync_FiltersByEitherPortSide()
    {
        using var context = CreateContext();
        var runtime = new TeamLabRuntime();
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        context.TeamLabTrafficFlows.AddRange(
            Flow(runtime, "10.0.0.1", 5020, "10.0.0.2", 443),
            Flow(runtime, "10.0.0.3", 8080, "10.0.0.4", 5020),
            Flow(runtime, "10.0.0.5", 1234, "10.0.0.6", 5678));
        await context.SaveChangesAsync();
        var service = CreateTrafficService(context);

        var page = await service.GetFlowsAsync(
            runtime.PublicId, null, 50, null, null, null, 5020, CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, flow => Assert.Contains(5020, new[] { flow.SourcePort!.Value, flow.DestinationPort!.Value }));
    }

    private static Guid Actor() => Guid.NewGuid();

    private static TeamLabTopologyNetworkModel Network() => new(
        "entry", "entry", new TeamLabAddressPoolModel("10.48.0.0/16", 24), true);

    private static TeamLabTopologyAssetModel Asset() => new(
        "asset-1",
        "资产",
        TeamLabAssetKind.Docker,
        1,
        new TeamLabAssetResourceModel(10, 512, 512),
        [new TeamLabTopologyInterfaceModel("eth0", "entry", 10, true)]);

    private static TeamLabTrafficCaptureJob CaptureJob(TeamLabRuntime runtime, string scope) => new()
    {
        RuntimeId = runtime.Id,
        Scope = scope,
        MaxBytes = 1024,
        MaxSeconds = 60
    };

    private static TeamLabTrafficFlow Flow(TeamLabRuntime runtime, string sourceIp, int sourcePort, string destinationIp, int destinationPort) => new()
    {
        RuntimeId = runtime.Id,
        SourceIp = sourceIp,
        SourcePort = sourcePort,
        DestinationIp = destinationIp,
        DestinationPort = destinationPort,
        Protocol = "TCP"
    };

    /// <summary>Clone and archive paths never touch release preparation, mirroring the existing null-service test convention.</summary>
    private static (TeamLabTopologyApplicationService Topologies, TeamLabReleaseService Releases) CreateTopologyService(AppDbContext context)
    {
        var validator = new TeamLabTopologyValidator();
        var releases = new TeamLabReleaseService(context, validator, null!);
        var topologyService = new TeamLabTopologyApplicationService(
            context, validator, releases, new TeamLabControlScopeService(context), new NodeCapacitySnapshotService(context));
        return (topologyService, releases);
    }

    private static TeamLabTrafficApplicationService CreateTrafficService(AppDbContext context)
    {
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        return new TeamLabTrafficApplicationService(
            context,
            Mock.Of<ITeamLabNodeExecutor>(),
            new LocalDevelopmentLeaseProvider(),
            Mock.Of<ITeamLabTrafficIngestor>(),
            new TeamLabEventRecorder(context, writer, new OperationalCorrelation()),
            NullLogger<TeamLabTrafficApplicationService>.Instance);
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"api-closure-{Guid.NewGuid():N}")
            .Options);
}
