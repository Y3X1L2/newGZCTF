using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Models.Data;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using GZCTF.Services.Vm;
using GZCTF.Storage;
using GZCTF.Middlewares;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Audit.Application;
using GZCTF.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly UserManager<UserInfo> _userManager;
    private readonly ImageTemplateDeletionService _deletionService;
    private readonly ImageImportApplicationService _imageImports;
    private readonly ImageDistributionService _imageDistribution;
    private readonly ILogger<ImageTemplateController> _logger;

    public ImageTemplateController(AppDbContext context, ImageStorage storage, IArchiveExtractor archiveExtractor,
        DockerImageRegistryService dockerRegistry,
        UserManager<UserInfo> userManager, ImageTemplateDeletionService deletionService,
        ImageImportApplicationService imageImports, ImageDistributionService imageDistribution,
        ILogger<ImageTemplateController> logger)
    {
        _context = context;
        _storage = storage;
        _archiveExtractor = archiveExtractor;
        _dockerRegistry = dockerRegistry;
        _userManager = userManager;
        _deletionService = deletionService;
        _imageImports = imageImports;
        _imageDistribution = imageDistribution;
        _logger = logger;
    }

    private async Task<UserInfo> CurrentUser() =>
        await _userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

    private static bool CanManageTemplate(UserInfo actor, ImageTemplate template) =>
        actor.Role >= Role.Admin || template.CreatedById == actor.Id;

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
            var actor = await CurrentUser();
            var imageTemplate = await _storage.SaveImageAsync(file);
            imageTemplate.CreatedById = actor.Id;
            _context.ImageTemplates.Add(imageTemplate);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Image template {Name} (ID:{Id}) uploaded by {User}",
                imageTemplate.Name, imageTemplate.Id, User.Identity?.Name);

            await _imageDistribution.DistributeToCapableNodesAsync(
                imageTemplate, HttpContext.RequestAborted);

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
        var actor = await CurrentUser();
        var query = _context.ImageTemplates.AsQueryable();
        if (actor.Role < Role.Admin)
            query = query.Where(template => template.CreatedById == null || template.CreatedById == actor.Id);

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
        var actor = await CurrentUser();
        var template = await _context.ImageTemplates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id &&
                                          (actor.Role >= Role.Admin ||
                                           item.CreatedById == null || item.CreatedById == actor.Id));
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
            var actor = await CurrentUser();
            var importer = HttpContext.RequestServices.GetRequiredService<Services.Vm.LocalImageImporter>();
            var template = await importer.ImportFromLocalPathAsync(
                request.LocalPath, request.DisplayName, actor.Id);

            await _imageDistribution.DistributeToCapableNodesAsync(
                template, HttpContext.RequestAborted);

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

        var actor = await CurrentUser();
        if (!string.IsNullOrWhiteSpace(request.RegistryAuth))
            return BadRequest(new
            {
                message = "Durable image imports do not accept persisted registry credentials."
            });

        try
        {
            var source = DockerImageReference.ResolvePullTarget(
                request.Name, request.RegistryUrl).FullImage;
            var imported = await _imageImports.ImportDockerReferenceNowAsync(
                new ActorContext(actor.Id, actor.Role),
                new DockerImageReferenceImportCommand(
                    request.Name, source, request.OSType, null),
                token);
            var template = await _context.ImageTemplates.SingleAsync(
                item => item.Id == imported.Id, token);
            await _imageDistribution.DistributeToCapableNodesAsync(template, token);
            return Ok(new { template.Id, template.Name, template.OSType, template.ImageType });
        }
        catch (ApiOperationTerminalException exception)
        {
            return Conflict(new { message = exception.Message, code = exception.Code });
        }
        catch (ApiContractException exception)
        {
            return BadRequest(new { message = exception.Message, code = exception.Code });
        }
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
        [FromForm] string? sourceImage,
        [FromForm] OSType osType,
        CancellationToken token)
    {
        var actor = await CurrentUser();
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        try
        {
            await using var stream = file.OpenReadStream();
            var imported = await _imageImports.ImportDockerArchiveNowAsync(
                new ActorContext(actor.Id, actor.Role),
                stream,
                file.FileName,
                file.Length,
                new DockerImageArchiveImportCommand(
                    name,
                    sourceImage,
                    osType,
                    null),
                token);
            var template = await _context.ImageTemplates.SingleAsync(
                item => item.Id == imported.Id, token);
            await _imageDistribution.DistributeToCapableNodesAsync(template, token);

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
            _logger.LogWarning(ex, "Docker archive upload failed for {TemplateName}", name);
            return BadRequest(new { message = ex.Message });
        }
        catch (ApiContractException ex)
        {
            _logger.LogWarning(ex, "Docker archive upload rejected for {TemplateName}", name);
            return StatusCode(ex.StatusCode, new { message = ex.Message, code = ex.Code });
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
        var actor = await CurrentUser();
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

            var result = await _archiveExtractor.ExtractAndRegisterAsync(
                archivePath, file.FileName, actor.Id, token);

            if (!result.Success)
                return BadRequest(new { message = result.Error });

            await _imageDistribution.DistributeToCapableNodesAsync(result.Template!, token);

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
    public async Task<IActionResult> Delete(int id, CancellationToken token)
    {
        var actor = await CurrentUser();
        var result = await _deletionService.DeleteAsync(
            id, new ActorContext(actor.Id, actor.Role), token);
        return result.Status switch
        {
            ImageTemplateDeleteStatus.NotFound => NotFound(),
            ImageTemplateDeleteStatus.Forbidden => Forbid(),
            ImageTemplateDeleteStatus.InUse => Conflict(new
            {
                message = "该模板仍被业务资源引用，无法删除。",
                references = result.References
            }),
            _ => NoContent()
        };
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

        Response.Headers.AcceptRanges = "bytes";
        return PhysicalFile(fullPath, "application/octet-stream", Path.GetFileName(fullPath), enableRangeProcessing: true);
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
