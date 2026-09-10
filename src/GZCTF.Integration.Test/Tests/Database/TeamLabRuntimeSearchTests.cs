using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public class TeamLabRuntimeSearchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("teamlab_search").WithUsername("postgres").WithPassword("postgres").WithCleanUp(true).Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task SearchTranslatesToSqlAndPagesCurrentGenerationFacts()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.EnsureCreatedAsync();
        var topology = new TeamLabTopology { Name = "search-scene" };
        var release = new TeamLabTopologyRelease { Topology = topology, Version = 1, CanonicalJson = "{}", ContentHash = new string('a', 64) };
        var node = new WorkerNode { Name = "Search-Worker", HostAddress = "127.0.0.1", AuthToken = "fixture" };
        db.TeamLabTopologyReleases.Add(release);
        db.WorkerNodes.Add(node);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            var runtime = new TeamLabRuntime { TopologyReleaseId = release.Id, ExternalReference = $"run-{i}", Generation = 2,
                Status = TeamLabRuntimeStatus.Running, CreatedAt = now, CreateRequestHash = Guid.NewGuid().ToString() };
            runtime.Shards.Add(new TeamLabRuntimeShard { Generation = 2, WorkerNode = node });
            runtime.Assets.Add(new TeamLabRuntimeAsset { Generation = 2, TopologyKey = "web", Name = "Web-Service" });
            runtime.Assets.Add(new TeamLabRuntimeAsset { Generation = 1, TopologyKey = "old", Name = "Legacy" });
            db.TeamLabRuntimes.Add(runtime);
        }
        await db.SaveChangesAsync();
        var service = new TeamLabAdminQueryService(db, null!, null!, null!);
        var query = new TeamLabRuntimeSearchQuery(Search: "WEB", Node: "worker", Generation: 2,
            Status: TeamLabRuntimeStatus.Running, ReleaseId: release.Id, Limit: 2);
        var first = await service.SearchRuntimesAsync(query, Guid.NewGuid(), true, default);
        Assert.Equal(2, first.Items.Count);
        Assert.All(first.Items, item => { Assert.Equal(topology.PublicId, item.TopologyId); Assert.Equal(1, item.AssetCount); });
        var second = await service.SearchRuntimesAsync(query with { After = first.NextCursor }, Guid.NewGuid(), true, default);
        Assert.Single(second.Items);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(item => item.Id).Distinct().Count());
        Assert.Empty((await service.SearchRuntimesAsync(query with { Search = "legacy" }, Guid.NewGuid(), true, default)).Items);
        Assert.Empty((await service.SearchRuntimesAsync(query, Guid.NewGuid(), false, default)).Items);
    }
}
