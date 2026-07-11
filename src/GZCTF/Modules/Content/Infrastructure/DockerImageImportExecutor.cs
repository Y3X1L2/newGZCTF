using System.Security.Cryptography;
using System.Text;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Services;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class DockerImageImportExecutor(
    DockerImageRegistryService registry,
    IImageImportStagingStore staging,
    DockerImageReferencePolicy referencePolicy) : IImageImportExecutor
{
    public async Task<ImageImportArtifact> ImportDockerReferenceAsync(
        ImageImportJob job,
        CancellationToken cancellationToken)
    {
        if (!job.CreatedById.HasValue)
            throw new ApiOperationTerminalException(
                "image_owner_missing", "The image owner no longer exists.");

        try
        {
            await referencePolicy.ValidateAsync(job.SourceReference, cancellationToken);
        }
        catch (ImageReferencePolicyException exception)
        {
            throw new ApiOperationTerminalException(exception.Code, exception.Message);
        }

        var nameHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(job.RequestedName)))[..24];
        var sourceHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(job.SourceReference)))[..16];
        var repository = $"imports/{job.CreatedById.Value:N}/{nameHash}/{sourceHash}";
        var imported = await registry.ImportReferenceAsync(
            job.SourceReference,
            repository,
            "latest",
            cancellationToken);
        var imageHash = (imported.Digest ?? imported.ImageId)?.Replace(
            "sha256:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();

        if (job.ExpectedDigest is { Length: > 0 } expected &&
            !string.Equals(expected, imageHash, StringComparison.OrdinalIgnoreCase))
            throw new ApiOperationTerminalException(
                "image_digest_mismatch", "The imported image digest does not match the expected digest.");

        return new ImageImportArtifact(
            imported.FullImage,
            imageHash,
            0,
            $"Imported from {job.SourceReference}");
    }

    public async Task<ImageImportArtifact> ImportDockerArchiveAsync(
        ImageImportJob job,
        CancellationToken cancellationToken)
    {
        if (!job.CreatedById.HasValue)
            throw new ApiOperationTerminalException(
                "image_owner_missing", "The image owner no longer exists.");
        await staging.VerifyAsync(job, cancellationToken);
        var nameHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(job.RequestedName)))[..24];
        var archiveHash = job.ExpectedDigest![..16];
        var repository = $"imports/{job.CreatedById.Value:N}/{nameHash}/{archiveHash}";
        var imported = await registry.ImportArchiveAsync(
            job.StagedPath!,
            repository,
            "latest",
            string.IsNullOrWhiteSpace(job.SourceReference) ? null : job.SourceReference,
            cancellationToken);
        var imageHash = (imported.Digest ?? imported.ImageId)?.Replace(
            "sha256:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();
        return new ImageImportArtifact(
            imported.FullImage,
            imageHash,
            job.ContentLength,
            $"Imported from {job.OriginalFileName}");
    }
}
