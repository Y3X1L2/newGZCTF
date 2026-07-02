using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Models.Data;
using GZCTF.Services;
using GZCTF.Services.Vm;
using GZCTF.Storage;
using GZCTF.Middlewares;
using GZCTF.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// Admin controller for managing environment image templates.
/// Supports uploading VM disk images (.qcow2/.ova/.vmdk) and listing available templates.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/image-templates")]
public class ImageTemplateController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ImageStorage _storage;
    private readonly IArchiveExtractor _archiveExtractor;
    private readonly DockerImageRegistryService _dockerRegistry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImageTemplateController> _logger;

    public ImageTemplateController(AppDbContext context, ImageStorage storage, IArchiveExtractor archiveExtractor,
        DockerImageRegistryService dockerRegistry, IServiceScopeFactory scopeFactory,
        ILogger<ImageTemplateController> logger)
    {
        _context = context;
        _storage = storage;
        _archiveExtractor = archiveExtractor;
        _dockerRegistry = dockerRegistry;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Upload a VM disk image file.
    /// </summary>
    [HttpPost]
    [RequireTeacher]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided");

        try
        {
            var imageTemplate = await _storage.SaveImageAsync(file);
            _context.ImageTemplates.Add(imageTemplate);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Image template {Name} (ID:{Id}) uploaded by {User}",
                imageTemplate.Name, imageTemplate.Id, User.Identity?.Name);

            QueueDistribution(imageTemplate.Id);

            return CreatedAtAction(nameof(GetById), new { id = imageTemplate.Id }, new
            {
                imageTemplate.Id, imageTemplate.Name, imageTemplate.OSType, imageTemplate.ImageType,
                imageTemplate.FileSize, imageTemplate.Status, imageTemplate.ImageHash, imageTemplate.UploadedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Image upload rejected: {Reason}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// List all image templates with optional filtering.
    /// </summary>
    [HttpGet]
    [RequireTeacher]
    public async Task<IActionResult> List(
        [FromQuery] OSType? osType = null,
        [FromQuery] ImageType? imageType = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.ImageTemplates
            .Where(t => t.TrainingCourseId == null);

        if (osType.HasValue)
            query = query.Where(t => t.OSType == osType.Value);

        if (imageType.HasValue)
            query = query.Where(t => t.ImageType == imageType.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search));

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await query.CountAsync();
        var templates = await query
            .OrderByDescending(t => t.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, items = templates.Select(t => new
        {
            t.Id, t.Name, t.OSType, t.ImageType, t.FileSize, t.Status,
            t.Description, t.ErrorMessage, t.ImageHash, t.UploadedAt, t.RegistryUrl
        }) });
    }

    /// <summary>
    /// Get a specific image template by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [RequireTeacher]
    public async Task<IActionResult> GetById(int id)
    {
        var template = await _context.ImageTemplates.FindAsync(id);
        if (template is null)
            return NotFound();

        return Ok(new
        {
            template.Id, template.Name, template.OSType, template.ImageType,
            template.FileSize, template.Status, template.Description,
            template.ErrorMessage, template.ContainsMalware, template.ImageHash, template.UploadedAt,
            template.RegistryUrl,
        });
    }

    /// <summary>
    /// Import VM image from local filesystem path.
    /// </summary>
    [HttpPost("import-local")]
    [RequireTeacher]
    public async Task<IActionResult> ImportFromLocal([FromBody] LocalImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LocalPath))
            return BadRequest(new { message = "Local path is required" });

        // Validate path is within allowed image directories
        var fullPath = Path.GetFullPath(request.LocalPath);
        var allowedRoots = new[]
        {
            Path.GetFullPath("./images"),
            Path.GetFullPath("/var/lib/gzctf/images"),
            Path.GetFullPath("/var/lib/libvirt/images"),
        };
        if (!allowedRoots.Any(r => fullPath.StartsWith(r + Path.DirectorySeparatorChar) || fullPath == r))
            return BadRequest(new { message = "Path is not in an allowed directory" });

        try
        {
            var importer = HttpContext.RequestServices.GetRequiredService<Services.Vm.LocalImageImporter>();
            var template = await importer.ImportFromLocalPathAsync(request.LocalPath, request.DisplayName);

            QueueDistribution(template.Id);

            return Ok(new
            {
                template.Id, template.Name, template.OSType, template.ImageType,
                template.FileSize, template.Status, template.ImageHash, template.UploadedAt
            });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Register a Docker image template from a registry URL.
    /// </summary>
    [HttpPost("register-docker")]
    [RequireTeacher]
    public async Task<IActionResult> RegisterDocker([FromBody] DockerRegisterRequest request, CancellationToken token)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var pullTarget = DockerImageReference.ResolvePullTarget(request.Name, request.RegistryUrl);
        var imageReference = pullTarget.FullImage;

        var existingTemplate = await _context.ImageTemplates
            .FirstOrDefaultAsync(t => t.ImageType == ImageType.Docker &&
                                      (t.Name == request.Name || t.RegistryUrl == imageReference), token);
        if (existingTemplate is not null && existingTemplate.Status != ImageStatus.Error)
            return BadRequest(new { message = "同名或同 Registry URL 的 Docker 模板已存在" });

        var template = existingTemplate ?? new ImageTemplate { ImageType = ImageType.Docker };
        template.Name = request.Name;
        template.OSType = request.OSType;
        template.RegistryUrl = imageReference;
        template.RegistryAuth = request.RegistryAuth;
        template.Status = ImageStatus.Importing;
        template.ErrorMessage = null;
        template.UploadedAt = DateTimeOffset.UtcNow;

        if (existingTemplate is null)
            _context.ImageTemplates.Add(template);
        await _context.SaveChangesAsync(token);

        var imageName = pullTarget.ImageName;
        var registryUrl = pullTarget.RegistryUrl;
        var registryAuth = request.RegistryAuth;
        var templateId = template.Id;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<ContainerOrchestrator>();
                await orchestrator.PullImageFromRegistryAsync(registryUrl, imageName, registryAuth);
                var t = await ctx.ImageTemplates.FindAsync(templateId);
                if (t is not null)
                {
                    t.Status = ImageStatus.Ready;
                    t.ErrorMessage = null;
                    await ctx.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                var t = await ctx.ImageTemplates.FindAsync(templateId);
                if (t is not null)
                {
                    t.Status = ImageStatus.Error;
                    t.ErrorMessage = TruncateError(ex.Message);
                    await ctx.SaveChangesAsync();
                }
                _logger.LogWarning(ex, "Failed to pull Docker image: {Image}", pullTarget.FullImage);
            }
        });

        return Ok(new { template.Id, template.Name, template.OSType, template.ImageType });
    }

    [HttpGet("docker-registry")]
    [RequireTeacher]
    public async Task<IActionResult> GetDockerRegistrySettings(CancellationToken token)
    {
        var endpoint = await _dockerRegistry.GetActiveEndpointAsync(token);
        return Ok(new
        {
            enabled = endpoint is not null,
            address = endpoint?.Address ?? string.Empty,
            @namespace = _dockerRegistry.RegistryNamespace,
            maxUploadSizeGb = _dockerRegistry.MaxUploadSizeGb
        });
    }

    /// <summary>
    /// Upload a docker save archive and push it to the configured internal registry.
    /// </summary>
    [HttpPost("upload-docker")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = 60L * 1024 * 1024 * 1024)]
    [RequireTeacher]
    public async Task<IActionResult> UploadDockerArchive(
        [FromForm] IFormFile file,
        [FromForm] string name,
        [FromForm] string repository,
        [FromForm] string tag,
        [FromForm] string? sourceImage,
        [FromForm] OSType osType,
        CancellationToken token)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });
        if (file.Length > _dockerRegistry.MaxUploadSizeBytes)
            return BadRequest(new { message = "Docker archive exceeds configured upload size limit" });
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Template display name is required" });

        var fileName = file.FileName.ToLowerInvariant();
        var ext = Path.GetExtension(fileName);
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            ext = ".tar.gz";
        else if (fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            ext = ".tgz";

        if (ext is not ".tar" and not ".tar.gz" and not ".tgz")
            return BadRequest(new { message = "Unsupported Docker archive format. Allowed: .tar, .tar.gz, .tgz" });

        string targetImage;
        try
        {
            targetImage = await _dockerRegistry.BuildInternalImageReferenceAsync(repository, tag, token);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var existingTemplate = await _context.ImageTemplates
            .FirstOrDefaultAsync(t => t.ImageType == ImageType.Docker &&
                                      (t.Name == name.Trim() || t.RegistryUrl == targetImage), token);
        if (existingTemplate is not null && existingTemplate.Status != ImageStatus.Error)
            return BadRequest(new { message = "同名或同 Registry URL 的 Docker 模板已存在" });

        var tempDir = Path.Combine(Path.GetTempPath(), "gzctf_docker_uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var archivePath = Path.Combine(tempDir, $"image{ext}");

        try
        {
            await using (var stream = file.OpenReadStream())
            await using (var fs = System.IO.File.Create(archivePath))
                await stream.CopyToAsync(fs, token);

            var result = await _dockerRegistry.ImportArchiveAsync(archivePath, repository, tag, sourceImage, token);
            var template = existingTemplate ?? new ImageTemplate { ImageType = ImageType.Docker };
            template.Name = name.Trim();
            template.OSType = osType;
            template.RegistryUrl = result.FullImage;
            template.RegistryAuth = null;
            template.Status = ImageStatus.Ready;
            template.ErrorMessage = null;
            template.UploadedAt = DateTimeOffset.UtcNow;
            template.FileSize = file.Length;
            template.ImageHash = result.ImageId?.Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);
            template.OriginalArchiveName = file.FileName;
            template.Description = $"Internal registry image loaded from {result.SourceImage}";

            if (existingTemplate is null)
                _context.ImageTemplates.Add(template);
            await _context.SaveChangesAsync(token);

            return Ok(new
            {
                template.Id,
                template.Name,
                template.OSType,
                template.ImageType,
                template.FileSize,
                template.Status,
                template.RegistryUrl,
                template.ErrorMessage,
                template.ImageHash
            });
        }
        catch (OperationCanceledException)
        {
            return BadRequest(new { message = "Docker image upload was cancelled" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Docker archive upload failed for {TargetImage}", targetImage);
            return BadRequest(new { message = ex.Message });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>
    /// Upload a VM image archive file (.zip, .tar.gz, .tar.xz).
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)] // 60GB
    [RequireTeacher]
    public async Task<IActionResult> UploadArchive(IFormFile file, CancellationToken token)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        var allowedExtensions = new[] { ".zip", ".tar.gz", ".tgz", ".tar.xz", ".txz" };
        var fileName = file.FileName.ToLowerInvariant();
        var ext = Path.GetExtension(fileName);

        if (fileName.EndsWith(".tar.gz"))
            ext = ".tar.gz";
        else if (fileName.EndsWith(".tar.xz"))
            ext = ".tar.xz";

        if (!allowedExtensions.Contains(ext))
            return BadRequest(new { message = $"Unsupported format. Allowed: {string.Join(", ", allowedExtensions)}" });

        // Save to temp path, then delegate to ArchiveExtractor for full pipeline
        var tempDir = Path.Combine(Path.GetTempPath(), "gzctf_uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var archivePath = Path.Combine(tempDir, $"archive{ext}");

        try
        {
            await using (var stream = file.OpenReadStream())
            await using (var fs = System.IO.File.Create(archivePath))
                await stream.CopyToAsync(fs, token);

            var result = await _archiveExtractor.ExtractAndRegisterAsync(archivePath, file.FileName, token);

            if (!result.Success)
                return BadRequest(new { message = result.Error });

            QueueDistribution(result.Template!.Id);

            return Ok(new { result.Template!.Id, result.Template.Name, result.Template.OSType, result.Template.ImageType, result.Template.FileSize });
        }
        finally
        {
            // Clean up temp archive
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Delete an image template and its stored file.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequireTeacher]
    public async Task<IActionResult> Delete(int id)
    {
        var template = await _context.ImageTemplates.FindAsync(id);
        if (template is null)
            return NotFound();

        var inUse = await _context.GameChallenges
            .AnyAsync(c => c.ImageTemplateId == id);

        if (inUse)
            return BadRequest(new { message = "该模板正在被题目使用，无法删除" });

        await _storage.DeleteImageAsync(template);
        _context.ImageTemplates.Remove(template);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Image template {Name} (ID:{Id}) deleted by {User}",
            template.Name, id, User.Identity?.Name);

        return NoContent();
    }

    [HttpGet("download/{hash}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadByHash([FromRoute] string hash, [FromQuery] Guid? nodeId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return BadRequest(new { message = "Image hash is required" });

        var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = Guid.TryParse(userId, out var parsedUserId) &&
                      await _context.Users.AsNoTracking()
                          .AnyAsync(u => u.Id == parsedUserId && u.Role >= Role.Admin, token);
        if (!isAdmin)
        {
            if (!nodeId.HasValue)
                return Unauthorized(new { message = "Node authentication is required" });

            var node = await _context.WorkerNodes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == nodeId.Value, token);
            if (node is null || !TryGetBearerToken(Request, out var authToken) ||
                !FixedTimeEquals(authToken, node.AuthToken))
                return Unauthorized(new { message = "Invalid node token" });
        }

        var template = await _context.ImageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ImageHash == hash, token);
        if (template is null || string.IsNullOrWhiteSpace(template.LocalFilePath))
            return NotFound(new { message = "Image template not found" });

        var fullPath = Path.GetFullPath(template.LocalFilePath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "Image file not found" });

        return PhysicalFile(fullPath, "application/octet-stream", Path.GetFileName(fullPath));
    }

    private void QueueDistribution(int templateId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var distributor = scope.ServiceProvider.GetService<Services.Fleet.ImageDistributionService>();
                if (distributor is null)
                    return;

                var template = await ctx.ImageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId);
                if (template is null)
                    return;

                await distributor.DistributeToCapableNodesAsync(template, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Image distribution task failed for template {TemplateId}", templateId);
            }
        });
    }

    private static bool TryGetBearerToken(HttpRequest request, out string token)
    {
        token = string.Empty;
        var header = request.Headers.Authorization.FirstOrDefault();
        if (!System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(header, out var value) ||
            !string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(value.Parameter))
            return false;

        token = value.Parameter.Trim();
        return true;
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string TruncateError(string? message)
    {
        var value = string.IsNullOrWhiteSpace(message) ? "Docker image operation failed." : message.Trim();
        return value.Length <= 1024 ? value : value[..1024];
    }
}

public class DockerRegisterRequest
{
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string RegistryUrl { get; set; } = string.Empty;

    public OSType OSType { get; set; } = OSType.Linux;

    [MaxLength(512)]
    public string? RegistryAuth { get; set; }
}

public class LocalImportRequest
{
    [Required]
    public string LocalPath { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
