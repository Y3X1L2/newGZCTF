using System;
using System.Linq;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabRuntimeSearchTests
{
    [Fact]
    public async Task SearchScopesOwnersAndIgnoresHistoricalAssetAndNodeMatches()
    {
        await using var db = Context();
        var owner = Guid.NewGuid();
        var mine = new TeamLabRuntime { CreatedById = owner, Generation = 2, ExternalReference = "course-run" };
        mine.Assets.Add(new TeamLabRuntimeAsset { Generation = 1, Name = "old-web", Status = TeamLabRuntimeStatus.Failed });
        mine.Assets.Add(new TeamLabRuntimeAsset { Generation = 2, Name = "new-db" });
        mine.Shards.Add(new TeamLabRuntimeShard { Generation = 1, WorkerNode = new WorkerNode { Name = "old-node" } });
        mine.Shards.Add(new TeamLabRuntimeShard { Generation = 2, WorkerNode = new WorkerNode { Name = "current-node" } });
        var other = new TeamLabRuntime { CreatedById = Guid.NewGuid(), ExternalReference = "game-run" };
        db.TeamLabRuntimes.AddRange(mine, other);
        await db.SaveChangesAsync();
        var service = new TeamLabAdminQueryService(db, null!, null!, null!);
        Assert.Single((await service.SearchRuntimesAsync(new(), owner, false, default)).Items);
        Assert.Equal(2, (await service.SearchRuntimesAsync(new(), owner, true, default)).Items.Count);
        Assert.Empty((await service.SearchRuntimesAsync(new(Search: "old-web"), owner, false, default)).Items);
        Assert.Empty((await service.SearchRuntimesAsync(new(Node: "old-node"), owner, false, default)).Items);
        Assert.Empty((await service.SearchRuntimesAsync(new(ErrorsOnly: true), owner, false, default)).Items);
        var match = Assert.Single((await service.SearchRuntimesAsync(new(Search: "NEW-DB", Node: "CURRENT", Generation: 2), owner, false, default)).Items);
        Assert.Equal(mine.PublicId, match.Id);
        Assert.Equal(1, match.AssetCount);
        Assert.Empty((await service.SearchRuntimesAsync(new(CreatedById: other.CreatedById), owner, false, default)).Items);
    }

    [Fact]
    public async Task SearchPagesSameTimestampWithoutDuplicatesAndFindsExactId()
    {
        await using var db = Context();
        var now = DateTimeOffset.UtcNow;
        db.TeamLabRuntimes.AddRange(Enumerable.Range(1, 3).Select(_ => new TeamLabRuntime { CreatedAt = now }));
        await db.SaveChangesAsync();
        var service = new TeamLabAdminQueryService(db, null!, null!, null!);
        var first = await service.SearchRuntimesAsync(new(Limit: 2), Guid.NewGuid(), true, default);
        var second = await service.SearchRuntimesAsync(new(After: first.NextCursor, Limit: 2), Guid.NewGuid(), true, default);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(item => item.Id).Distinct().Count());
        Assert.Null(second.NextCursor);
        Assert.Single((await service.SearchRuntimesAsync(new(Search: first.Items[0].Id.ToString()), Guid.NewGuid(), true, default)).Items);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(101, null)]
    [InlineData(20, "invalid")]
    public async Task InvalidQueryIsRejected(int limit, string? cursor)
    {
        await using var db = Context();
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => new TeamLabAdminQueryService(db, null!, null!, null!)
            .SearchRuntimesAsync(new(After: cursor, Limit: limit), Guid.NewGuid(), true, default));
        Assert.Equal(400, error.StatusCode);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
