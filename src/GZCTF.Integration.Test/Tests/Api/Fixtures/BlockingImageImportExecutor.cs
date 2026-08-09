using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;

namespace GZCTF.Integration.Test.Tests.Api.Fixtures;

public sealed class BlockingImageImportExecutor : IImageImportExecutor
{
    public TaskCompletionSource<Guid> Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ImageImportArtifact> ImportDockerReferenceAsync(
        ImageImportJob job,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public async Task<ImageImportArtifact> ImportDockerArchiveAsync(
        ImageImportJob job,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(job.StagedPath))
            throw new InvalidOperationException("The staged archive was not persisted.");

        Started.TrySetResult(job.OperationId);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The blocking executor was released without cancellation.");
    }

    public Task<ImageImportArtifact> ImportVmQcow2Async(
        ImageImportJob job,
        CancellationToken cancellationToken) => ImportDockerArchiveAsync(job, cancellationToken);
}
