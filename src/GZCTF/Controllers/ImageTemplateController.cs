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
