using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public sealed class ApiOperationQueryTests
{
    [Fact]
    public async Task ListForToken_IsolatesFiltersAndPaginatesByStableCursor()
    {
        await using var context = CreateContext();
        var tokenId = Guid.CreateVersion7();
        var otherTokenId = Guid.CreateVersion7();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expectedFirst = Operation(tokenId, "00000000-0000-0000-0000-000000000030", "teamlab.runtime.v1",
            ApiOperationStatus.Pending, createdAt);
        var expectedSecond = Operation(tokenId, "00000000-0000-0000-0000-000000000020", "teamlab.runtime.v1",
            ApiOperationStatus.Pending, createdAt);
        context.ApiOperations.AddRange(
            expectedFirst,
            expectedSecond,
            Operation(tokenId, null, "image.import", ApiOperationStatus.Succeeded, createdAt.AddMinutes(-1)),
            Operation(otherTokenId, "00000000-0000-0000-0000-000000000040", "teamlab.runtime.v1",
                ApiOperationStatus.Pending, createdAt));
        await context.SaveChangesAsync();
        var service = new ApiOperationService(new EfApiOperationStore(context));

        var firstPage = await service.ListForTokenAsync(
            tokenId, ApiOperationStatus.Pending, " teamlab.runtime.v1 ", null, 1, CancellationToken.None);
        Assert.Equal(expectedFirst.Id, Assert.Single(firstPage.Items).Id);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await service.ListForTokenAsync(
            tokenId,
            ApiOperationStatus.Pending,
            "teamlab.runtime.v1",
            firstPage.NextCursor,
            1,
            CancellationToken.None);
        Assert.Equal(expectedSecond.Id, Assert.Single(secondPage.Items).Id);
        Assert.Null(secondPage.NextCursor);
        Assert.All(firstPage.Items.Concat(secondPage.Items), item => Assert.Equal(tokenId, item.ApiTokenId));
    }

    [Fact]
    public async Task ListForToken_RejectsInvalidCursor()
    {
        await using var context = CreateContext();
        var service = new ApiOperationService(new EfApiOperationStore(context));

        var exception = await Assert.ThrowsAsync<ApiOperationQueryException>(() =>
            service.ListForTokenAsync(
                Guid.CreateVersion7(), null, null, "not-a-cursor", 50, CancellationToken.None));
        Assert.Equal("operation_cursor_invalid", exception.Code);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"operation-query-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static ApiOperation Operation(
        Guid tokenId,
        string? id,
        string kind,
        ApiOperationStatus status,
        DateTimeOffset createdAt) => new()
    {
        Id = id is null ? Guid.CreateVersion7() : Guid.Parse(id),
        ApiTokenId = tokenId,
        Kind = kind,
        Status = status,
        RouteKey = Guid.NewGuid().ToString("N"),
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        RequestHash = Guid.NewGuid().ToString("N"),
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };
}
