using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Storage;
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
    private readonly ILogger<ImageTemplateController> _logger;

    public ImageTemplateController(AppDbContext context, ImageStorage storage, ILogger<ImageTemplateController> logger)
    {
        _context = context;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Upload a VM disk image file.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Author")]
    [RequestSizeLimit(52_428_800_000)] // 50GB * 1.05 headroom
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

            return CreatedAtAction(nameof(GetById), new { id = imageTemplate.Id }, imageTemplate);
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

        return Ok(new { total, page, pageSize, items = templates });
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

        return Ok(template);
    }

    /// <summary>
    /// Import VM image from local filesystem path.
    /// </summary>
    [HttpPost("import-local")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportFromLocal([FromBody] LocalImportRequest request)
    {
        try
        {
            var importer = HttpContext.RequestServices.GetRequiredService<Services.Vm.LocalImageImporter>();
            var template = await importer.ImportFromLocalPathAsync(request.LocalPath, request.DisplayName);
            return Ok(template);
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
    [Authorize(Roles = "Admin")]
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
            Status = ImageStatus.Ready,
            UploadedAt = DateTimeOffset.UtcNow,
        };

        _context.ImageTemplates.Add(template);
        await _context.SaveChangesAsync(token);

        return Ok(new { template.Id, template.Name, template.OSType, template.ImageType });
    }

    /// <summary>
    /// Upload a VM image archive file (.zip, .tar.gz, .tar.xz).
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)] // 60GB
    [Authorize(Roles = "Admin")]
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

        var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "images");
        Directory.CreateDirectory(storagePath);

        var guid = Guid.NewGuid().ToString("N");
        var templateDir = Path.Combine(storagePath, guid);
        Directory.CreateDirectory(templateDir);

        var archivePath = Path.Combine(templateDir, $"archive{ext}");
        await using (var stream = file.OpenReadStream())
        await using (var fs = System.IO.File.Create(archivePath))
            await stream.CopyToAsync(fs, token);

        if (ext == ".zip")
        {
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, templateDir, true);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"ZIP extraction failed: {ex.Message}" });
            }
        }

        var template = new ImageTemplate
        {
            Name = Path.GetFileNameWithoutExtension(file.FileName),
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            LocalFilePath = templateDir,
            OriginalArchiveName = file.FileName,
            FileSize = file.Length,
            Status = ImageStatus.Ready,
            UploadedAt = DateTimeOffset.UtcNow,
        };

        _context.ImageTemplates.Add(template);
        await _context.SaveChangesAsync(token);

        return Ok(new { template.Id, template.Name, template.OSType, template.ImageType, template.FileSize });
    }

    /// <summary>
    /// Delete an image template and its stored file.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var template = await _context.ImageTemplates.FindAsync(id);
        if (template is null)
            return NotFound();

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
}

public class LocalImportRequest
{
    [Required]
    public string LocalPath { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
