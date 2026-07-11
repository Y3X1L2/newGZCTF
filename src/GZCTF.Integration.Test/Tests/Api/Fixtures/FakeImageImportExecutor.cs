using System.Collections.Concurrent;
using System.Security.Cryptography;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api.Fixtures;

public sealed class FakeImageImportExecutor : IImageImportExecutor
{
    private readonly ConcurrentDictionary<Guid, int> _executions = new();

    public Task<ImageImportArtifact> ImportDockerReferenceAsync(
        ImageImportJob job,
        CancellationToken cancellationToken)
    {
        _executions.AddOrUpdate(job.OperationId, 1, (_, count) => count + 1);
        return Task.FromResult(new ImageImportArtifact(
            $"gzctf-internal://test/{job.OperationId:N}:latest",
            new string('a', 64),
            1024,
            $"Imported from {job.SourceReference}"));
    }

    public async Task<ImageImportArtifact> ImportDockerArchiveAsync(
        ImageImportJob job,
        CancellationToken cancellationToken)
    {
        Assert.True(File.Exists(job.StagedPath));
        await using var stream = File.OpenRead(job.StagedPath!);
        var digest = Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream, cancellationToken));
        Assert.Equal(job.ExpectedDigest, digest);
        _executions.AddOrUpdate(job.OperationId, 1, (_, count) => count + 1);
        return new ImageImportArtifact(
            $"gzctf-internal://test/{job.OperationId:N}:latest",
            new string('b', 64),
            job.ContentLength,
            $"Imported from {job.OriginalFileName}");
    }

    public int ExecutionCount(Guid operationId) =>
        _executions.TryGetValue(operationId, out var count) ? count : 0;
}
