using System.Security.Cryptography;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public sealed record VmArtifactDownload(
    string DownloadUrl,
    string Sha256,
    long Size);

public class VmArtifactStore(
    IOptions<DockerRegistrySettings> options,
    VmImageRegistryService registry,
    ILogger<VmArtifactStore> logger)
{
    public async Task<VmArtifactDownload> ValidateAndBuildDownloadAsync(ImageTemplate template, Guid nodeId,
        CancellationToken token)
    {
        if (template.ImageType == ImageType.Docker)
            throw new InvalidOperationException("Docker image templates cannot be used as VM artifacts.");
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new InvalidOperationException($"VM template {template.Name} ({template.Id}) has no image hash.");
        if (string.IsNullOrWhiteSpace(template.LocalFilePath))
            throw new InvalidOperationException($"VM template {template.Name} ({template.Id}) has no local file path.");

        var path = Path.GetFullPath(template.LocalFilePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"VM template file for {template.Name} ({template.Id}) was not found.", path);

        var info = new FileInfo(path);
        if (template.FileSize > 0 && template.FileSize != info.Length)
            throw new InvalidOperationException(
                $"VM template {template.Name} ({template.Id}) size mismatch: database={template.FileSize}, file={info.Length}.");

        var expectedHash = NormalizeSha256(template.ImageHash);
        var actualHash = await ComputeSha256Async(path, token);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"VM template {template.Name} ({template.Id}) sha256 mismatch: database={expectedHash}, file={actualHash}.");

        var reference = await registry.EnsureArtifactAsync(template, token);
        var downloadUrl = BuildRegistryBlobUrl(reference, options.Value.NormalizedAddress, actualHash);

        logger.LogDebug("VM template {TemplateId} validated for node {NodeId}: {Hash} ({Size} bytes)",
            template.Id, nodeId, actualHash, info.Length);

        return new VmArtifactDownload(downloadUrl, actualHash, info.Length);
    }

    public async Task<VmArtifactDownload> EnsureAndBuildDownloadAsync(ImageTemplate template, Guid nodeId,
        CancellationToken token)
    {
        if (template.ImageType == ImageType.Docker)
            throw new InvalidOperationException("Docker image templates cannot be used as VM artifacts.");
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new InvalidOperationException($"VM template {template.Name} ({template.Id}) has no image hash.");
        if (template.FileSize <= 0)
            throw new InvalidOperationException($"VM template {template.Name} ({template.Id}) has no valid file size.");

        var expectedHash = NormalizeSha256(template.ImageHash);
        var reference = await registry.EnsureArtifactAsync(template, token);
        var downloadUrl = BuildRegistryBlobUrl(reference, options.Value.NormalizedAddress, expectedHash);

        logger.LogDebug("VM template {TemplateId} storage artifact ready for node {NodeId}: {Hash} ({Size} bytes)",
            template.Id, nodeId, expectedHash, template.FileSize);

        return new VmArtifactDownload(downloadUrl, expectedHash, template.FileSize);
    }

    static string BuildRegistryBlobUrl(VmImageArtifactReference reference, string registryAddress, string sha256) =>
        $"http://{registryAddress}/v2/{reference.Repository}/blobs/sha256:{sha256}";

    static async Task<string> ComputeSha256Async(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    static string NormalizeSha256(string value)
    {
        value = value.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        if (value.Length != 64)
            throw new InvalidOperationException("VM image sha256 digest is invalid.");
        return value.ToLowerInvariant();
    }
}
