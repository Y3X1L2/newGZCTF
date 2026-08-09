using System.Security.Cryptography;
using System.Text;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Content.Infrastructure;

public interface IVmQcow2ImageImportExecutor
{
    Task<ImageImportArtifact> ImportAsync(ImageImportJob job, CancellationToken cancellationToken);
}

public sealed class VmQcow2ImageImportExecutor(
    OciArtifactRegistryClient registry,
    IImageImportStagingStore staging,
    IOptions<DockerRegistrySettings> registryOptions) : IVmQcow2ImageImportExecutor
{
    const string ArtifactType = "application/vnd.gzctf.vm-template.qcow2";
    const string BlobMediaType = "application/octet-stream";
    readonly DockerRegistrySettings _settings = registryOptions.Value;

    public async Task<ImageImportArtifact> ImportAsync(
        ImageImportJob job,
        CancellationToken cancellationToken)
    {
        if (!job.CreatedById.HasValue)
            throw new ApiOperationTerminalException(
                "image_owner_missing", "The image owner no longer exists.");
        if (job.SourceKind != ImageImportSourceKind.VmQcow2 ||
            string.IsNullOrWhiteSpace(job.StagedPath) ||
            string.IsNullOrWhiteSpace(job.ExpectedDigest))
            throw new ApiOperationTerminalException(
                "vm_image_source_invalid", "The staged qcow2 source is incomplete.");

        await staging.VerifyAsync(job, cancellationToken);
        var digest = OciArtifactRegistryClient.NormalizeDigest(job.ExpectedDigest);
        var nameHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(job.RequestedName)))[..24];
        var path = $"gzctf/vm-imports/{job.CreatedById.Value:N}/{nameHash}";
        var repository = string.IsNullOrWhiteSpace(_settings.NormalizedNamespace)
            ? path
            : $"{_settings.NormalizedNamespace}/{path}";
        var tag = digest;
        var artifact = await registry.PushFileAsync(
            _settings.NormalizedAddress,
            repository,
            tag,
            job.StagedPath,
            digest,
            ArtifactType,
            BlobMediaType,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["org.gzctf.vm-template.sha256"] = digest,
                ["org.gzctf.vm-template.source"] = "external-qcow2"
            },
            cancellationToken);
        var registryUrl = $"{artifact.RegistryAddress}/{artifact.Repository}:{artifact.Tag}";
        return new ImageImportArtifact(
            registryUrl,
            digest,
            artifact.Size,
            $"Imported immutable qcow2 from {job.OriginalFileName}",
            new ImportedVmArtifact(
                artifact.RegistryAddress,
                artifact.Repository,
                artifact.Tag,
                digest,
                artifact.Size));
    }
}
