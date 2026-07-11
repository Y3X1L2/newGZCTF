using System.Buffers;
using System.Security.Cryptography;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class FileImageImportStagingStore : IImageImportStagingStore
{
    private static readonly string[] AllowedExtensions = [".tar", ".tar.gz", ".tgz"];
    private readonly string _root;
    private readonly long _maxUploadSize;

    public FileImageImportStagingStore(
        IHostEnvironment environment,
        IOptions<DockerRegistrySettings> settings)
    {
        _root = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath, "files", "staging", "image-imports"));
        _maxUploadSize = settings.Value.MaxUploadSizeBytes;
    }

    public async Task<StagedImageImport> StageAsync(
        Stream source,
        string originalFileName,
        long declaredLength,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(originalFileName);
        var extension = GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(safeName) || extension is null)
            throw new ImageImportContractException(
                "image_archive_invalid",
                "Unsupported Docker archive format. Allowed: .tar, .tar.gz, .tgz.",
                400);
        if (declaredLength is <= 0 || declaredLength > _maxUploadSize)
            throw new ImageImportContractException(
                "image_archive_size_invalid",
                "Docker archive size is invalid or exceeds the configured limit.",
                400);

        Directory.CreateDirectory(_root);
        var id = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(_root, $"{id}.partial");
        var finalPath = Path.Combine(_root, $"{id}{extension}");
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            long written = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             buffer.Length,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    written += read;
                    if (written > _maxUploadSize)
                        throw new ImageImportContractException(
                            "image_archive_size_invalid",
                            "Docker archive exceeds the configured upload limit.",
                            400);
                    hash.AppendData(buffer.AsSpan(0, read));
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await target.FlushAsync(cancellationToken);
                target.Flush(true);
            }

            if (written != declaredLength)
                throw new ImageImportContractException(
                    "image_archive_size_invalid",
                    "Docker archive length does not match the uploaded content.",
                    400);
            var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            var normalizedExpected = NormalizeDigest(expectedDigest);
            if (normalizedExpected is not null &&
                !string.Equals(normalizedExpected, digest, StringComparison.Ordinal))
                throw new ImageImportContractException(
                    "image_digest_mismatch",
                    "The uploaded archive digest does not match the expected digest.",
                    400);

            File.Move(temporaryPath, finalPath);
            return new StagedImageImport(finalPath, safeName, written, digest);
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(finalPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task VerifyAsync(ImageImportJob job, CancellationToken cancellationToken)
    {
        var path = ResolveManagedPath(job.StagedPath);
        if (path is null || !File.Exists(path) || job.ContentLength <= 0)
            throw InvalidStaging();

        var file = new FileInfo(path);
        if (file.Length != job.ContentLength)
            throw InvalidStaging();
        await using var stream = File.OpenRead(path);
        var digest = Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(digest, NormalizeDigest(job.ExpectedDigest), StringComparison.Ordinal))
            throw InvalidStaging();
    }

    public Task DeleteAsync(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var managedPath = ResolveManagedPath(path);
        if (managedPath is not null)
            TryDelete(managedPath);
        return Task.CompletedTask;
    }

    public Task<int> DeleteUnreferencedAsync(
        IReadOnlySet<string> activePaths,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
            return Task.FromResult(0);

        var protectedPaths = activePaths
            .Select(ResolveManagedPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToHashSet(PathComparer);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (protectedPaths.Contains(path) ||
                File.GetLastWriteTimeUtc(path) > olderThan.UtcDateTime)
                continue;
            if (TryDelete(path))
                removed++;
        }

        return Task.FromResult(removed);
    }

    private string? ResolveManagedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, PathComparison)
            ? fullPath
            : null;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string? GetExtension(string fileName) =>
        AllowedExtensions.FirstOrDefault(extension =>
            fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeDigest(string? value)
    {
        var digest = value?.Trim().ToLowerInvariant();
        if (digest?.StartsWith("sha256:", StringComparison.Ordinal) == true)
            digest = digest[7..];
        return string.IsNullOrWhiteSpace(digest) ? null : digest;
    }

    private static ApiOperationTerminalException InvalidStaging() => new(
        "image_staging_invalid",
        "The staged image archive is missing or does not match its persisted metadata.");

    private static bool TryDelete(string path)
    {
        try
        {
            var existed = File.Exists(path);
            File.Delete(path);
            return existed && !File.Exists(path);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
