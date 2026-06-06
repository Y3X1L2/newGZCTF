using System.Diagnostics;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Storage;

/// <summary>
/// Manages VM disk image storage on the local filesystem.
/// Handles upload validation, file storage, and integration with libvirt storage pools.
/// </summary>
public class ImageStorage
{
    private readonly ILogger<ImageStorage> _logger;
    private readonly string _storagePath;
    private readonly long _maxUploadSizeBytes;
    private readonly int _timeoutSeconds;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".qcow2", ".ova", ".vmdk", ".img"
    };

    /// <summary>
    /// Initializes a new instance of <see cref="ImageStorage"/> with KVM configuration and structured logging.
    /// </summary>
    /// <param name="settings">KVM configuration options from the "KvmSettings" configuration section.</param>
    /// <param name="logger">Structured logger for operation auditing.</param>
    public ImageStorage(IOptions<KvmSettings> settings, ILogger<ImageStorage> logger)
    {
        _logger = logger;
        var cfg = settings.Value;
        _storagePath = string.IsNullOrWhiteSpace(cfg.ImageStoragePath)
            ? "/var/lib/gzctf/images"
            : cfg.ImageStoragePath;
        _maxUploadSizeBytes = cfg.MaxUploadSizeGb > 0
            ? (long)cfg.MaxUploadSizeGb * 1024 * 1024 * 1024
            : 50L * 1024 * 1024 * 1024;
        _timeoutSeconds = cfg.OperationTimeoutSeconds > 0 ? cfg.OperationTimeoutSeconds : 120;

        EnsureStorageDirectoryExists();
    }

    /// <summary>
    /// Saves an uploaded VM image file to local storage after validating format and size.
    /// Registers the image with the libvirt default storage pool via <c>virsh pool-refresh</c>.
    /// </summary>
    /// <param name="file">The uploaded form file containing the VM disk image.</param>
    /// <returns>An <see cref="ImageTemplate"/> entity representing the stored image.</returns>
    /// <exception cref="ImageStorageException">Thrown when validation fails or the storage operation fails.</exception>
    public async Task<ImageTemplate> SaveImageAsync(IFormFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        _logger.LogInformation("Processing image upload: '{FileName}' ({FileSize} bytes)",
            file.FileName, file.Length);

        // Validate file name
        if (string.IsNullOrWhiteSpace(file.FileName))
            throw new ImageStorageException("File name is required.");

        // Validate file format
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            _logger.LogWarning("Rejected upload '{FileName}': unsupported format '{Extension}'",
                file.FileName, extension);
            throw new ImageStorageException(
                $"Unsupported image format '{extension}'. Allowed formats: {string.Join(", ", AllowedExtensions)}");
        }

        // Validate file size
        if (file.Length <= 0)
            throw new ImageStorageException("Uploaded file is empty.");

        if (file.Length > _maxUploadSizeBytes)
        {
            var maxGb = _maxUploadSizeBytes / (1024L * 1024 * 1024);
            _logger.LogWarning("Rejected upload '{FileName}': size {FileSize} exceeds {MaxSize}GB limit",
                file.FileName, file.Length, maxGb);
            throw new ImageStorageException(
                $"File size ({file.Length} bytes) exceeds the maximum allowed size of {maxGb} GB.");
        }

        // Determine image type from file extension
        var imageType = extension.ToLowerInvariant() switch
        {
            ".qcow2" => ImageType.Qcow2,
            ".ova" => ImageType.Ova,
            ".vmdk" => ImageType.Vmdk,
            _ => ImageType.Qcow2
        };

        // Generate a sanitized file name to prevent path traversal and collisions
        var safeName = GenerateSafeFileName(file.FileName);
        var filePath = Path.Combine(_storagePath, safeName);

        // Save the file to disk
        try
        {
            await using var sourceStream = file.OpenReadStream();
            await using var destStream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 8192, useAsync: true);
            await sourceStream.CopyToAsync(destStream);
        }
        catch (Exception ex) when (ex is not ImageStorageException)
        {
            _logger.LogError(ex, "Failed to write image file to '{FilePath}'", filePath);
            SafeDeleteFile(filePath);
            throw new ImageStorageException($"Failed to save image file: {ex.Message}", ex);
        }

        _logger.LogDebug("Image file written to '{FilePath}'", filePath);

        // Compute SHA256 hash of the saved file
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            await using var fs = File.OpenRead(filePath);
            hash = Convert.ToHexString(await sha.ComputeHashAsync(fs)).ToLowerInvariant();
        }

        // Detect OS type from filename
        var lowerName = file.FileName.ToLowerInvariant();
        var osType = lowerName.Contains("windows") || lowerName.Contains("winserver")
            || lowerName.Contains("winsrv") || lowerName.Contains("wkdb")
            ? OSType.Windows : OSType.Linux;

        // Register with libvirt storage pool
        try
        {
            await RefreshLibvirtPoolAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh libvirt storage pool after saving '{FilePath}'. " +
                                   "The image was saved but is not registered in the pool.", filePath);
            // Non-fatal: the file is correctly saved; pool refresh can be retried later
        }

        var image = new ImageTemplate
        {
            Name = Path.GetFileNameWithoutExtension(safeName),
            OSType = osType,
            ImageType = imageType,
            LocalFilePath = filePath,
            ImageHash = hash,
            FileSize = file.Length,
            UploadedAt = DateTimeOffset.UtcNow,
            Status = ImageStatus.Ready
        };

        _logger.LogInformation("Image '{ImageName}' saved successfully at '{FilePath}' ({FileSize} bytes)",
            image.Name, filePath, file.Length);

        return image;
    }

    /// <summary>
    /// Deletes a VM image from local storage and removes it from the libvirt storage pool.
    /// </summary>
    /// <param name="image">The image template entity to delete.</param>
    /// <exception cref="ImageStorageException">Thrown when the file cannot be deleted.</exception>
    public async Task DeleteImageAsync(ImageTemplate image)
    {
        ArgumentNullException.ThrowIfNull(image);

        _logger.LogInformation("Deleting image '{ImageName}' (ID: {ImageId})", image.Name, image.Id);

        // Delete the primary disk file
        if (!string.IsNullOrWhiteSpace(image.LocalFilePath) && File.Exists(image.LocalFilePath))
        {
            try
            {
                File.Delete(image.LocalFilePath);
                _logger.LogDebug("Deleted image file '{FilePath}'", image.LocalFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete image file '{FilePath}'", image.LocalFilePath);
                throw new ImageStorageException($"Failed to delete image file: {ex.Message}", ex);
            }
        }
        else
        {
            _logger.LogWarning("Image file not found on disk for '{ImageName}' at '{FilePath}'",
                image.Name, image.LocalFilePath);
        }

        // Clean up associated libvirt XML definition if present
        if (!string.IsNullOrWhiteSpace(image.LocalFilePath))
        {
            var xmlPath = Path.ChangeExtension(image.LocalFilePath, ".xml");
            if (File.Exists(xmlPath))
            {
                try
                {
                    File.Delete(xmlPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete associated XML definition '{Path}'", xmlPath);
                }
            }
        }

        // Refresh libvirt pool to reflect the deletion
        try
        {
            await RefreshLibvirtPoolAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh libvirt storage pool after deletion. " +
                                   "The pool metadata may be stale.");
        }

        _logger.LogInformation("Image '{ImageName}' deleted successfully", image.Name);
    }

    /// <summary>
    /// Returns the full local filesystem path for a stored image file.
    /// </summary>
    /// <param name="fileName">The image file name.</param>
    /// <returns>The full path to the image file within the configured storage directory.</returns>
    public string GetImagePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var safeName = GenerateSafeFileName(fileName);
        var path = Path.Combine(_storagePath, safeName);

        _logger.LogDebug("Resolved image path for '{FileName}' => '{Path}'", fileName, path);
        return path;
    }

    /// <summary>
    /// Refreshes the libvirt default storage pool so it recognizes newly added or removed images.
    /// </summary>
    private async Task RefreshLibvirtPoolAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "virsh",
                    Arguments = "pool-refresh default",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var error = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("virsh pool-refresh failed (exit code {ExitCode}): {Error}",
                    process.ExitCode, error.Trim());
            }
            else
            {
                _logger.LogDebug("libvirt storage pool 'default' refreshed successfully");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("virsh pool-refresh timed out after {Timeout}s", _timeoutSeconds);
        }
    }

    /// <summary>
    /// Generates a sanitized file name by removing invalid characters and appending a unique suffix
    /// to prevent path traversal and filename collisions.
    /// </summary>
    private static string GenerateSafeFileName(string originalName)
    {
        var name = Path.GetFileName(originalName);

        // Replace characters invalid in file names
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
            name = name.Replace(c, '_');

        // Limit length to avoid filesystem issues
        if (name.Length > 200)
            name = name[..200];

        // Append a short unique suffix to avoid collisions
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var ext = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);

        return $"{baseName}_{uniqueSuffix}{ext}";
    }

    /// <summary>
    /// Ensures the storage directory exists on disk, creating it if necessary.
    /// </summary>
    private void EnsureStorageDirectoryExists()
    {
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
            _logger.LogInformation("Created image storage directory at '{Path}'", _storagePath);
        }
    }

    /// <summary>
    /// Attempts to delete a file without throwing on failure (best-effort cleanup).
    /// </summary>
    private void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up file '{Path}'", path);
        }
    }
}

/// <summary>
/// Exception thrown when an image storage operation fails.
/// Wraps file system and validation errors for consistent upstream handling.
/// </summary>
public class ImageStorageException : Exception
{
    /// <summary>
    /// Creates a new <see cref="ImageStorageException"/> with the specified error message.
    /// </summary>
    public ImageStorageException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="ImageStorageException"/> with the specified error message and inner exception.
    /// </summary>
    public ImageStorageException(string message, Exception innerException) : base(message, innerException) { }
}
