using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Services;
using GZCTF.Services.Vm;
using GZCTF.Storage;
using GZCTF.Middlewares;
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
    private readonly ILogger<ImageTemplateController> _logger;

    public ImageTemplateController(AppDbContext context, ImageStorage storage, IArchiveExtractor archiveExtractor, ILogger<ImageTemplateController> logger)
    {
        _context = context;
        _storage = storage;
        _archiveExtractor = archiveExtractor;
        _logger = logger;
    }

    /// <summary>
    /// Upload a VM disk image file.
    /// </summary>
    [HttpPost]
    [RequireAdmin]
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
    public async Task<IActionResult> List(
        [FromQuery] OSType? osType = null,
        [FromQuery] ImageType? imageType = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.ImageTemplates.AsQueryable();

        if (osType.HasValue)
            query = query.Where(t => t.OSType == osType.Value);

        if (imageType.HasValue)
            query = query.Where(t => t.ImageType == imageType.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search));

        var total = await query.CountAsync();
        var templates = await query
            .OrderByDescending(t => t.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, items = templates.Select(t => new
        {
            t.Id, t.Name, t.OSType, t.ImageType, t.FileSize, t.Status,
            t.Description, t.ImageHash, t.UploadedAt, t.RegistryUrl
        }) });
    }

    /// <summary>
    /// Get a specific image template by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var template = await _context.ImageTemplates.FindAsync(id);
        if (template is null)
            return NotFound();

        return Ok(new
        {
            template.Id, template.Name, template.OSType, template.ImageType,
            template.FileSize, template.Status, template.Description,
            template.ContainsMalware, template.ImageHash, template.UploadedAt,
            template.RegistryUrl,
        });
    }

    /// <summary>
    /// Import VM image from local filesystem path.
    /// </summary>
    [HttpPost("import-local")]
    [RequireAdmin]
    public async Task<IActionResult> ImportFromLocal([FromBody] LocalImportRequest request)
    {
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
    [RequireAdmin]
    public async Task<IActionResult> RegisterDocker([FromBody] DockerRegisterRequest request, CancellationToken token)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var template = new ImageTemplate
        {
            Name = request.Name,
            OSType = request.OSType,
            ImageType = ImageType.Docker,
            RegistryUrl = request.RegistryUrl,
            RegistryAuth = request.RegistryAuth,
            Status = ImageStatus.Ready,
            UploadedAt = DateTimeOffset.UtcNow,
        };

        _context.ImageTemplates.Add(template);
        await _context.SaveChangesAsync(token);

        var sp = HttpContext.RequestServices;
        var imageName = request.Name;
        var registryUrl = request.RegistryUrl;
        var registryAuth = request.RegistryAuth;
        _ = Task.Run(async () =>
        {
            try
            {
                var orchestrator = sp.GetRequiredService<ContainerOrchestrator>();
                await orchestrator.PullImageFromRegistryAsync(
                    registryUrl ?? "", imageName, registryAuth);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to pull Docker image: {Name}", imageName);
            }
        });

        return Ok(new { template.Id, template.Name, template.OSType, template.ImageType });
    }

    /// <summary>
    /// Upload a VM image archive file (.zip, .tar.gz, .tar.xz).
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)] // 60GB
    [RequireAdmin]
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
    [RequireAdmin]
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
