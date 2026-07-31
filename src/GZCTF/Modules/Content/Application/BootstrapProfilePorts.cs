using GZCTF.Modules.Content.Domain;

namespace GZCTF.Modules.Content.Application;

public sealed record StagedBootstrapArtifact(string Path, string Digest, long Size);

public interface IBootstrapProfileArtifactStagingService
{
    Task<StagedBootstrapArtifact> StageAsync(
        Stream source,
        string fileName,
        long declaredLength,
        string? expectedDigest,
        CancellationToken cancellationToken);

    Task DeleteStagedAsync(string? path, CancellationToken cancellationToken);
}

public interface IBootstrapProfileDistributionService
{
    Task<IReadOnlyList<BootstrapProfileDistribution>> QueueAndDistributeAsync(
        long profileVersionId,
        CancellationToken cancellationToken);

    Task DeleteVersionCachesAsync(
        BootstrapProfileVersion version,
        CancellationToken cancellationToken);
}
