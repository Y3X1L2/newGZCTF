using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public sealed class ImageTemplateCatalogDeletionTests
{
    [Fact]
    public async Task CompleteDeletionAsync_KeepsDeletingIntentWhenArtifactCleanupFails()
    {
        await using var context = CreateContext();
        context.ImageTemplates.Add(CreateTemplate());
        await context.SaveChangesAsync();
        var catalog = new EfImageTemplateCatalog(
            context,
            new StubArtifactCleaner(new InvalidOperationException("registry unavailable")));

        await catalog.MarkDeletingAsync(
            7,
            _ => Task.FromResult(new ImageTemplateDeleteDecision(true, [])),
            CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.CompleteDeletionAsync(7, CancellationToken.None));

        var template = await context.ImageTemplates.SingleAsync(template => template.Id == 7);
        Assert.Equal(ImageStatus.Deleting, template.Status);
        Assert.Contains("registry unavailable", template.ErrorMessage);
    }

    [Fact]
    public async Task CompleteDeletionAsync_RemovesTemplateOnlyAfterArtifactCleanupSucceeds()
    {
        await using var context = CreateContext();
        context.ImageTemplates.Add(CreateTemplate());
        await context.SaveChangesAsync();
        var cleaner = new StubArtifactCleaner();
        var catalog = new EfImageTemplateCatalog(context, cleaner);

        await catalog.MarkDeletingAsync(
            7,
            _ => Task.FromResult(new ImageTemplateDeleteDecision(true, [])),
            CancellationToken.None);
        await catalog.CompleteDeletionAsync(7, CancellationToken.None);

        Assert.Equal(1, cleaner.CallCount);
        Assert.False(await context.ImageTemplates.AnyAsync(template => template.Id == 7));
    }

    private static ImageTemplate CreateTemplate() => new()
    {
        Id = 7,
        Name = "template",
        ImageType = ImageType.Docker,
        OSType = OSType.Linux,
        RegistryUrl = "gzctf-internal://ctf/imports/template:latest",
        Status = ImageStatus.Ready
    };

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class StubArtifactCleaner(Exception? exception = null) : IImageTemplateArtifactCleaner
    {
        public int CallCount { get; private set; }

        public Task CleanupAsync(ImageTemplate template, CancellationToken cancellationToken)
        {
            CallCount++;
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }
}
