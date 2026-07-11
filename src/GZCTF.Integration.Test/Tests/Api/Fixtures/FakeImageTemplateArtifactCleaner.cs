using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;

namespace GZCTF.Integration.Test.Tests.Api.Fixtures;

public sealed class FakeImageTemplateArtifactCleaner : IImageTemplateArtifactCleaner
{
    public Task CleanupAsync(ImageTemplate template, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
