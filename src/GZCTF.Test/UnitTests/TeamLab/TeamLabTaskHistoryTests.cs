using System;
using System.Linq;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabTaskHistoryTests
{
    [Fact]
    public async Task HistoryIsScopedAndPagesAcrossGenerationsWithoutDuplicatingEqualTimestamps()
    {
        await using var db = Context();
        var runtime = new TeamLabRuntime();
        var foreign = new TeamLabRuntime();
        db.TeamLabRuntimes.AddRange(runtime, foreign);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 3; i++) db.DeploymentQueueTickets.Add(new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.TeamLabRuntime, TeamLabRuntimeId = runtime.Id, Generation = i,
            CreatedAt = now, ProtectedPayload = "must-not-be-returned", ErrorMessage = "private-detail"
        });
        db.DeploymentQueueTickets.Add(new DeploymentQueueTicket { Kind = DeploymentQueueKind.TeamLabRuntime, TeamLabRuntimeId = foreign.Id });
        await db.SaveChangesAsync();
        var service = new TeamLabAdminQueryService(db, null!, null!, null!);
        var first = await service.ListRuntimeTasksAsync(runtime.PublicId, null, null, 2, default);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var second = await service.ListRuntimeTasksAsync(runtime.PublicId, null, first.NextCursor, 2, default);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(item => item.Id).Distinct().Count());
        Assert.Single((await service.ListRuntimeTasksAsync(runtime.PublicId, 2, null, 20, default)).Items);
        var serialized = System.Text.Json.JsonSerializer.Serialize(first);
        Assert.DoesNotContain("must-not-be-returned", serialized);
        Assert.DoesNotContain("private-detail", serialized);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(101, null)]
    [InlineData(20, "invalid-cursor")]
    public async Task InvalidPaginationIsRejected(int limit, string? after)
    {
        await using var db = Context();
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            new TeamLabAdminQueryService(db, null!, null!, null!).ListRuntimeTasksAsync(Guid.NewGuid(), null, after, limit, default));
        Assert.Equal(400, error.StatusCode);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
