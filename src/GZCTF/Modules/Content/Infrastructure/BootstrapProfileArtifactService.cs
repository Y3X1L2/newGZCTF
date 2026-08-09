using System.Buffers;
using System.Security.Cryptography;
using GZCTF.Models.Internal;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class BootstrapProfileArtifactService : IBootstrapProfileArtifactStagingService
{
    public const string ArtifactType = "application/vnd.gzctf.bootstrap-profile.v1";
    public const string BlobMediaType = "application/vnd.gzctf.bootstrap-profile.layer.v1+tar+gzip";
    private readonly string _stagingRoot;
    private readonly DockerRegistrySettings _settings;
    private readonly OciArtifactRegistryClient _registry;

    public BootstrapProfileArtifactService(
        IHostEnvironment environment,
        IOptions<DockerRegistrySettings> settings,
        OciArtifactRegistryClient registry)
    {
        _settings = settings.Value;
        _registry = registry;
        _stagingRoot = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath, "files", "staging", "bootstrap-profiles"));
    }

    public async Task<StagedBootstrapArtifact> StageAsync(
        Stream source,
        string fileName,
        long declaredLength,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            !(safeName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
              safeName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)))
            throw new BootstrapProfileContractException(
                "bootstrap_artifact_invalid", "Bootstrap artifact must be a .tar.gz or .tgz archive.", 400);
        if (declaredLength is <= 0 || declaredLength > _settings.MaxUploadSizeBytes)
            throw new BootstrapProfileContractException(
                "bootstrap_artifact_size_invalid", "Bootstrap artifact size is invalid.", 400);

        Directory.CreateDirectory(_stagingRoot);
        var target = Path.Combine(_stagingRoot, $"{Guid.CreateVersion7():N}.tar.gz");
        var temporary = target + ".partial";
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            long written = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    written += read;
                    if (written > _settings.MaxUploadSizeBytes)
                        throw new BootstrapProfileContractException(
                            "bootstrap_artifact_size_invalid", "Bootstrap artifact exceeds the upload limit.", 400);
                    hash.AppendData(buffer.AsSpan(0, read));
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                output.Flush(true);
            }
            if (written != declaredLength)
                throw new BootstrapProfileContractException(
                    "bootstrap_artifact_size_invalid", "Bootstrap artifact length does not match the upload.", 400);
            var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.IsNullOrWhiteSpace(expectedDigest) &&
                !string.Equals(OciArtifactRegistryClient.NormalizeDigest(expectedDigest), digest,
                    StringComparison.Ordinal))
                throw new BootstrapProfileContractException(
                    "bootstrap_artifact_digest_mismatch",
                    "Bootstrap artifact digest does not match expectedDigest.", 400);
            File.Move(temporary, target);
            return new StagedBootstrapArtifact(target, digest, written);
        }
        catch
        {
            TryDelete(temporary);
            TryDelete(target);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<OciArtifactReference> PublishAsync(
        BootstrapProfileOperationJob job,
        CancellationToken cancellationToken)
    {
        if (job.Action != BootstrapProfileOperationAction.PublishVersion ||
            string.IsNullOrWhiteSpace(job.ArtifactDigest) ||
            !job.Version.HasValue)
            throw new InvalidOperationException("Bootstrap profile publish job is incomplete.");
        var reference = BuildReference(job.ProfilePublicId, job.Version.Value, job.ArtifactDigest, job.ArtifactSize);
        if (await _registry.ExistsAsync(reference, cancellationToken)) return reference;
        if (string.IsNullOrWhiteSpace(job.StagedArtifactPath))
            throw new InvalidOperationException("Bootstrap profile staged artifact is missing.");
        return await _registry.PushFileAsync(
            reference.RegistryAddress,
            reference.Repository,
            reference.Tag,
            ResolveStagedPath(job.StagedArtifactPath),
            job.ArtifactDigest,
            ArtifactType,
            BlobMediaType,
            new Dictionary<string, string>
            {
                ["org.gzctf.bootstrap-profile.id"] = job.ProfilePublicId.ToString("D"),
                ["org.gzctf.bootstrap-profile.version"] = job.Version.Value.ToString()
            },
            cancellationToken);
    }

    public OciArtifactReference BuildReference(Guid profileId, int version, string digest, long size)
    {
        var path = $"gzctf/bootstrap-profile/{profileId:N}";
        var repository = string.IsNullOrWhiteSpace(_settings.NormalizedNamespace)
            ? path
            : $"{_settings.NormalizedNamespace}/{path}";
        return new OciArtifactReference(
            _settings.NormalizedAddress,
            repository,
            version.ToString(),
            $"sha256:{OciArtifactRegistryClient.NormalizeDigest(digest)}",
            size);
    }

    public Task DeletePublishedAsync(BootstrapProfileVersion version, CancellationToken cancellationToken) =>
        _registry.DeleteAsync(BuildReference(
            version.Profile.PublicId, version.Version, version.ArtifactDigest, version.ArtifactSize),
            cancellationToken);

    public Task DeleteStagedAsync(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(path)) TryDelete(ResolveStagedPath(path));
        return Task.CompletedTask;
    }

    private string ResolveStagedPath(string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = _stagingRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, comparison))
            throw new InvalidOperationException("Bootstrap artifact staging path escaped its managed root.");
        return full;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
