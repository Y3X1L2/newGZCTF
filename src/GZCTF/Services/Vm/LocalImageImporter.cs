using System.Security.Cryptography;
using GZCTF.Models.Data;

namespace GZCTF.Services.Vm;

/// <summary>
/// Imports VM disk images from LOCAL filesystem paths.
/// Supports: single .qcow2/.ova/.vmdk/.img files, or directories containing them.
/// Key import paths: "D:\wkdb-winserver2012-挖矿病毒模拟" etc.
/// After import, registers ImageTemplate in DB for VM provisioning.
/// </summary>
public class LocalImageImporter
{
    private readonly AppDbContext _context;
    private readonly ILogger<LocalImageImporter> _logger;
    private readonly string _storagePath;

    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".qcow2", ".ova", ".vmdk", ".img" };

    public LocalImageImporter(
        AppDbContext context,
        IConfiguration config,
        ILogger<LocalImageImporter> logger)
    {
        _context = context;
        _logger = logger;
        _storagePath = config["KvmSettings:ImageStoragePath"] ?? "/var/lib/gzctf/images";
    }

    /// <summary>
    /// Import a VM image from a LOCAL path into the platform's image storage.
    /// The file is COPIED (not moved) to the storage directory.
    /// SHA256 hash is computed for integrity verification and caching.
    /// </summary>
    /// <param name="localPath">Filesystem path to the VM image or directory</param>
    /// <param name="displayName">Human-readable name (default: filename without extension)</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Created ImageTemplate entity</returns>
    public async Task<ImageTemplate> ImportFromLocalPathAsync(
        string localPath, string? displayName = null, CancellationToken token = default)
    {
        _logger.LogInformation("Importing VM image from local path: {Path}", localPath);

        // Step 1: Validate path exists
        if (!File.Exists(localPath) && !Directory.Exists(localPath))
            throw new FileNotFoundException($"Image path not found: {localPath}");

        // Step 2: Determine if file or directory, find the actual image file
        string sourceFile;
        if (Directory.Exists(localPath))
        {
            // Search directory for supported image files
            var files = Directory.GetFiles(localPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            if (files.Count == 0)
                throw new InvalidOperationException(
                    $"No supported image files found in directory: {localPath}. Supported: {string.Join(", ", SupportedExtensions)}");

            sourceFile = files.OrderByDescending(f => new FileInfo(f).Length).First();
            _logger.LogInformation("Found image file in directory: {File}", sourceFile);
        }
        else
        {
            sourceFile = localPath;
        }

        // Step 3: Validate file extension
        var ext = Path.GetExtension(sourceFile);
        if (!SupportedExtensions.Contains(ext))
            throw new InvalidOperationException(
                $"Unsupported image format: {ext}. Supported: {string.Join(", ", SupportedExtensions)}");

        // Step 4: Generate safe destination name
        var originalName = displayName ?? Path.GetFileNameWithoutExtension(sourceFile);
        var safeName = GenerateSafeFileName(sourceFile);
        var destPath = Path.Combine(_storagePath, safeName);

        // Step 5: Ensure storage directory exists
        if (!Directory.Exists(_storagePath))
            Directory.CreateDirectory(_storagePath);

        // Step 6: Copy file to storage (with progress logging)
        var fileInfo = new FileInfo(sourceFile);
        _logger.LogInformation("Copying {Size}MB from {Source} to {Dest}",
            fileInfo.Length / 1024 / 1024, sourceFile, destPath);

        using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
        using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
        {
            await sourceStream.CopyToAsync(destStream, token);
        }

        // Step 7: Compute SHA256 hash
        var hash = await ComputeSha256Async(destPath, token);
        _logger.LogInformation("Image SHA256: {Hash}", hash);

        // Step 8: Detect OS type from filename heuristics
        var osType = DetectOsType(originalName);

        // Step 9: Determine image type from extension
        var imageType = ext.ToLowerInvariant() switch
        {
            ".qcow2" => ImageType.Qcow2,
            ".ova" => ImageType.Ova,
            ".vmdk" => ImageType.Vmdk,
            _ => ImageType.Qcow2
        };

        // Step 10: Create ImageTemplate record
        var template = new ImageTemplate
        {
            Name = originalName,
            OSType = osType,
            ImageType = imageType,
            LocalFilePath = destPath,
            FileSize = fileInfo.Length,
            UploadedAt = DateTimeOffset.UtcNow,
            Status = ImageStatus.Ready,
            Description = $"Imported from local: {Path.GetFileName(localPath)}"
        };

        _context.ImageTemplates.Add(template);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation("Image '{Name}' imported successfully (ID: {Id}, Hash: {Hash})",
            template.Name, template.Id, hash);

        return template;
    }

    /// <summary>
    /// Detect OS type from image filename using heuristics.
    /// Windows keywords: windows, win, winserver, winsrv, wkdb
    /// Default: Linux
    /// </summary>
    public static OSType DetectOsType(string name)
    {
        var lowered = name.ToLowerInvariant();
        if (lowered.Contains("windows") || lowered.Contains("win") ||
            lowered.Contains("winserver") || lowered.Contains("winsrv") ||
            lowered.Contains("wkdb"))
            return OSType.Windows;
        return OSType.Linux;
    }

    /// <summary>
    /// Generate a safe, unique filename for storing in the image directory.
    /// Keeps the original base name (sanitized) plus a short UUID suffix.
    /// </summary>
    public static string GenerateSafeFileName(string originalPath)
    {
        var ext = Path.GetExtension(originalPath);
        var unique = Guid.NewGuid().ToString("N")[..8];
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        // Keep baseName readable but safe
        var safe = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        if (safe.Length > 100) safe = safe[..100];
        return $"{safe}_{unique}{ext}";
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken token)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        var hash = await sha.ComputeHashAsync(stream, token);
        return Convert.ToHexStringLower(hash);
    }
}
