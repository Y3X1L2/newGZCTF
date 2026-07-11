using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Modules.Penetration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Penetration;

public sealed class PenetrationImageTemplateReferenceProviderTests
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetReferencesAsync_ReturnsCurrentTopologyNodeReference()
    {
        await using var context = CreateContext();
        context.PenetrationNodes.Add(new PenetrationNode
        {
            Id = 11,
            Name = "Current entry",
            ImageTemplateId = 7
        });
        await context.SaveChangesAsync();

        var references = await CreateProvider(context).GetReferencesAsync(7, CancellationToken.None);

        var reference = Assert.Single(references);
        Assert.Equal("topology-node", reference.ResourceType);
        Assert.Equal("11", reference.ResourceId);
        Assert.Equal("Current entry", reference.DisplayName);
    }

    [Fact]
    public async Task GetReferencesAsync_ReturnsPublishedSnapshotReferenceWithoutCurrentNode()
    {
        await using var context = CreateContext();
        context.PenetrationPublishedSnapshots.Add(CreateSnapshot(21, 3, 7));
        await context.SaveChangesAsync();

        var references = await CreateProvider(context).GetReferencesAsync(7, CancellationToken.None);

        var reference = Assert.Single(references);
        Assert.Equal("published-snapshot", reference.ResourceType);
        Assert.Equal("21:3", reference.ResourceId);
        Assert.Contains("21", reference.DisplayName, StringComparison.Ordinal);
        Assert.Contains("v3", reference.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReferencesAsync_DeduplicatesSnapshotAndKeepsCurrentAndPublishedSourcesIdentifiable()
    {
        await using var context = CreateContext();
        context.PenetrationNodes.Add(new PenetrationNode
        {
            Id = 12,
            Name = "Current core",
            ImageTemplateId = 7
        });
        context.PenetrationPublishedSnapshots.Add(CreateSnapshot(22, 4, 7, 7));
        await context.SaveChangesAsync();

        var references = await CreateProvider(context).GetReferencesAsync(7, CancellationToken.None);

        Assert.Collection(
            references,
            reference =>
            {
                Assert.Equal("published-snapshot", reference.ResourceType);
                Assert.Equal("22:4", reference.ResourceId);
            },
            reference =>
            {
                Assert.Equal("topology-node", reference.ResourceType);
                Assert.Equal("12", reference.ResourceId);
            });
    }

    [Fact]
    public async Task GetReferencesAsync_DoesNotMatchUnrelatedPublishedTemplate()
    {
        await using var context = CreateContext();
        context.PenetrationPublishedSnapshots.Add(CreateSnapshot(23, 5, 9));
        await context.SaveChangesAsync();

        var references = await CreateProvider(context).GetReferencesAsync(7, CancellationToken.None);

        Assert.Empty(references);
    }

    [Theory]
    [InlineData("{ malformed")]
    [InlineData("{}")]
    public async Task GetReferencesAsync_ReturnsFailClosedReferenceForMalformedSnapshot(string snapshotJson)
    {
        await using var context = CreateContext();
        context.PenetrationPublishedSnapshots.Add(new PenetrationPublishedSnapshot
        {
            Id = 105,
            GameId = 24,
            PublishedVersion = 6,
            SnapshotJson = snapshotJson
        });
        await context.SaveChangesAsync();

        var references = await CreateProvider(context).GetReferencesAsync(7, CancellationToken.None);

        var reference = Assert.Single(references);
        Assert.Equal("published-snapshot-invalid", reference.ResourceType);
        Assert.Equal("24:6", reference.ResourceId);
        Assert.Contains("invalid", reference.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static PenetrationImageTemplateReferenceProvider CreateProvider(AppDbContext context) => new(context);

    private static PenetrationPublishedSnapshot CreateSnapshot(
        int gameId,
        int publishedVersion,
        params int[] imageTemplateIds) =>
        new()
        {
            GameId = gameId,
            PublishedVersion = publishedVersion,
            SnapshotJson = JsonSerializer.Serialize(new PenetrationConfigModel
            {
                GameId = gameId,
                PublishedVersion = publishedVersion,
                Nodes = imageTemplateIds.Select((id, index) => new PenetrationNodeModel
                {
                    Id = index + 1,
                    Name = $"Published node {index + 1}",
                    ImageTemplateId = id
                }).ToList()
            }, SnapshotJsonOptions)
        };

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
