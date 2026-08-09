using System.Collections.Concurrent;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Content.Domain;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public sealed record VmImageArtifactReference(
    string RegistryAddress,
    string Repository,
    string Tag,
    string Digest);

public class VmImageRegistryService(
    IOptions<DockerRegistrySettings> options,
    OciArtifactRegistryClient registry)
{
    private const string ArtifactType = "application/vnd.gzctf.vm-template.qcow2";
    private const string BlobMediaType = "application/octet-stream";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ArtifactGates =
        new(StringComparer.Ordinal);
    private readonly DockerRegistrySettings _settings = options.Value;

    public VmImageArtifactReference BuildReference(ImageTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new InvalidOperationException($"VM template {template.Id} has no image hash.");
        return new VmImageArtifactReference(
            _settings.NormalizedAddress,
            BuildRepository(template.Id),
            template.ImageHash,
            $"sha256:{OciArtifactRegistryClient.NormalizeDigest(template.ImageHash)}");
    }

    public virtual async Task<VmImageArtifactReference> EnsureArtifactAsync(
        ImageTemplate template,
        CancellationToken token = default)
    {
        if (template.ImageType == ImageType.Docker)
            throw new InvalidOperationException("Docker image templates cannot be pushed as VM artifacts.");
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new InvalidOperationException($"VM template {template.Name} ({template.Id}) has no image hash.");
        var reference = BuildReference(template);
        var gateKey = $"{reference.RegistryAddress}/{reference.Repository}:{reference.Tag}";
        var gate = ArtifactGates.GetOrAdd(gateKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            if (await ArtifactExistsAsync(template, token)) return reference;
            if (string.IsNullOrWhiteSpace(template.LocalFilePath))
                throw new InvalidOperationException(
                    $"VM template {template.Name} ({template.Id}) has no local file path for registry bootstrap.");
            var artifact = await registry.PushFileAsync(
                reference.RegistryAddress,
                reference.Repository,
                reference.Tag,
                template.LocalFilePath,
                template.ImageHash,
                ArtifactType,
                BlobMediaType,
                new Dictionary<string, string>
                {
                    ["org.gzctf.vm-template.id"] = template.Id.ToString(),
                    ["org.gzctf.vm-template.sha256"] = OciArtifactRegistryClient.NormalizeDigest(template.ImageHash)
                },
                token);
            return new VmImageArtifactReference(
                artifact.RegistryAddress, artifact.Repository, artifact.Tag, artifact.Digest);
        }
        finally
        {
            gate.Release();
        }
    }

    public virtual Task<bool> ArtifactExistsAsync(ImageTemplate template, CancellationToken token = default)
    {
        if (template.FileSize <= 0)
            throw new InvalidOperationException($"VM template {template.Name} ({template.Id}) has no valid file size.");
        var reference = BuildReference(template);
        return registry.ExistsAsync(new OciArtifactReference(
            reference.RegistryAddress,
            reference.Repository,
            reference.Tag,
            reference.Digest,
            template.FileSize), token);
    }

    public virtual Task DeleteArtifactAsync(ImageTemplate template, CancellationToken token = default)
    {
        if (template.ImageType == ImageType.Docker || string.IsNullOrWhiteSpace(template.ImageHash))
            return Task.CompletedTask;
        if (template.PreparedArtifact is { Status: VmPreparedArtifactStatus.Ready } prepared &&
            prepared.ArtifactSize > 0 &&
            !string.IsNullOrWhiteSpace(prepared.RegistryAddress) &&
            !string.IsNullOrWhiteSpace(prepared.RegistryRepository) &&
            !string.IsNullOrWhiteSpace(prepared.RegistryTag) &&
            !string.IsNullOrWhiteSpace(prepared.ArtifactDigest))
            return registry.DeleteAsync(new OciArtifactReference(
                prepared.RegistryAddress,
                prepared.RegistryRepository,
                prepared.RegistryTag,
                $"sha256:{OciArtifactRegistryClient.NormalizeDigest(prepared.ArtifactDigest)}",
                prepared.ArtifactSize), token);
        var reference = BuildReference(template);
        return registry.DeleteAsync(new OciArtifactReference(
            reference.RegistryAddress,
            reference.Repository,
            reference.Tag,
            reference.Digest,
            template.FileSize), token);
    }

    private string BuildRepository(int templateId)
    {
        var path = $"gzctf/vm-template/{templateId}";
        return string.IsNullOrWhiteSpace(_settings.NormalizedNamespace)
            ? path
            : $"{_settings.NormalizedNamespace}/{path}";
    }
}
