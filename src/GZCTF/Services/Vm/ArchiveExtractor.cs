using System.Diagnostics;
using System.Security.Cryptography;
using GZCTF.Models.Data;

namespace GZCTF.Services.Vm;

public interface IArchiveExtractor
{
    Task<ArchiveExtractResult> ExtractAndRegisterAsync(string archivePath, string originalFileName, CancellationToken token);
}

public class ArchiveExtractResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ImageTemplate? Template { get; set; }
}

public class ArchiveExtractor : IArchiveExtractor
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArchiveExtractor> _logger;
    private readonly string _storagePath;

    public ArchiveExtractor(AppDbContext context, IConfiguration configuration, ILogger<ArchiveExtractor> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _storagePath = configuration.GetValue<string>("KvmSettings:ImageStoragePath") ?? "./images";
    }

    public async Task<ArchiveExtractResult> ExtractAndRegisterAsync(string archivePath, string originalFileName, CancellationToken token)
    {
        var guid = Guid.NewGuid().ToString("N");
        var extractDir = Path.Combine(_storagePath, guid);
        Directory.CreateDirectory(extractDir);

        try
        {
            // Extract based on extension
            var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
            if (originalFileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                ext = ".tar.gz";
            else if (originalFileName.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase))
                ext = ".tar.xz";

            var success = ext switch
            {
                ".zip" => await RunCommandAsync("unzip", $"-o \"{archivePath}\" -d \"{extractDir}\"", token),
                ".tar.gz" or ".tgz" => await RunCommandAsync("tar", $"-xzf \"{archivePath}\" -C \"{extractDir}\"", token),
                ".tar.xz" or ".txz" => await RunCommandAsync("tar", $"-xJf \"{archivePath}\" -C \"{extractDir}\"", token),
                _ => false
            };

            if (!success)
                return new ArchiveExtractResult { Success = false, Error = "Extraction failed" };

            // Detect VM format
            var allFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
            var hasVmx = allFiles.Any(f => f.EndsWith(".vmx", StringComparison.OrdinalIgnoreCase));
            var hasVmdk = allFiles.Any(f => f.EndsWith(".vmdk", StringComparison.OrdinalIgnoreCase) && !f.Contains("-s0"));
            var hasQcow2 = allFiles.Any(f => f.EndsWith(".qcow2", StringComparison.OrdinalIgnoreCase));
            var hasOva = allFiles.Any(f => f.EndsWith(".ova", StringComparison.OrdinalIgnoreCase));

            var qcow2Path = Path.Combine(extractDir, "disk.qcow2");

            if (hasOva)
            {
                var ovaDir = Path.Combine(extractDir, "ova_extracted");
                Directory.CreateDirectory(ovaDir);
                var ovaFile = allFiles.First(f => f.EndsWith(".ova", StringComparison.OrdinalIgnoreCase));
                await RunCommandAsync("tar", $"-xf \"{ovaFile}\" -C \"{ovaDir}\"", token);
                allFiles = Directory.GetFiles(ovaDir, "*.*", SearchOption.AllDirectories);
                hasVmdk = allFiles.Any(f => f.EndsWith(".vmdk", StringComparison.OrdinalIgnoreCase));
            }

            if (hasVmx && hasVmdk)
            {
                var baseVmdk = allFiles.First(f =>
                    f.EndsWith(".vmdk", StringComparison.OrdinalIgnoreCase) &&
                    !f.Contains("-s0") && !f.Contains("-flat"));
                await RunCommandAsync("qemu-img", $"convert -f vmdk -O qcow2 \"{baseVmdk}\" \"{qcow2Path}\"", token);
            }
            else if (hasQcow2 && !hasVmdk)
            {
                var qcow2 = allFiles.First(f => f.EndsWith(".qcow2", StringComparison.OrdinalIgnoreCase));
                if (!string.Equals(qcow2, qcow2Path, StringComparison.OrdinalIgnoreCase))
                    File.Move(qcow2, qcow2Path);
            }
            else if (hasVmdk)
            {
                var baseVmdk = allFiles.First(f =>
                    f.EndsWith(".vmdk", StringComparison.OrdinalIgnoreCase) &&
                    !f.Contains("-s0") && !f.Contains("-flat"));
                await RunCommandAsync("qemu-img", $"convert -f vmdk -O qcow2 \"{baseVmdk}\" \"{qcow2Path}\"", token);
            }

            // Detect OS
            var osType = OSType.Windows;
            var lowerFiles = string.Join(" ", allFiles).ToLowerInvariant();
            if (lowerFiles.Contains("linux") || lowerFiles.Contains("ubuntu") || lowerFiles.Contains("centos") ||
                lowerFiles.Contains("debian"))
                osType = OSType.Linux;

            // SHA256
            var hash = "";
            if (File.Exists(qcow2Path))
            {
                using var sha = SHA256.Create();
                await using var fs = File.OpenRead(qcow2Path);
                hash = Convert.ToHexString(await sha.ComputeHashAsync(fs, token)).ToLowerInvariant();
            }

            var fileSize = new FileInfo(qcow2Path).Length;

            var template = new ImageTemplate
            {
                Name = Path.GetFileNameWithoutExtension(originalFileName),
                OSType = osType,
                ImageType = ImageType.Qcow2,
                LocalFilePath = qcow2Path,
                ImageHash = hash,
                OriginalArchiveName = originalFileName,
                FileSize = fileSize,
                Status = ImageStatus.Ready,
                UploadedAt = DateTimeOffset.UtcNow,
            };

            _context.ImageTemplates.Add(template);
            await _context.SaveChangesAsync(token);

            return new ArchiveExtractResult { Success = true, Template = template };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive extraction failed for {File}", originalFileName);
            return new ArchiveExtractResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<bool> RunCommandAsync(string cmd, string args, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var process = Process.Start(psi);
        if (process is null)
            return false;
        await process.WaitForExitAsync(token);
        return process.ExitCode == 0;
    }
}
