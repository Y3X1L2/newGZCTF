using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Services.Docker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/v1/docker")]
[Produces(MediaTypeNames.Application.Json)]
public class DockerController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly DockerComposeDeployer _deployer;
    private readonly DockerImageBuilder _builder;

    public DockerController(AppDbContext context, DockerComposeDeployer deployer, DockerImageBuilder builder)
    { _context = context; _deployer = deployer; _builder = builder; }

    [HttpGet("images")]
    [Authorize]
    public async Task<IActionResult> ListImages()
    {
        var images = await _context.DockerImages.ToListAsync();
        return Ok(images);
    }

    [HttpPost("images")]
    [RequireAdmin]
    public async Task<IActionResult> CreateImage([FromBody] DockerImage image)
    {
        _context.DockerImages.Add(image);
        await _context.SaveChangesAsync();
        return Ok(image);
    }

    [HttpDelete("images/{id:int}")]
    [RequireAdmin]
    public async Task<IActionResult> DeleteImage(int id)
    {
        var image = await _context.DockerImages.FindAsync(id);
        if (image is null) return NotFound();
        _context.DockerImages.Remove(image);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("deploy")]
    [RequireAdmin]
    public async Task<IActionResult> Deploy([FromBody] DeployRequest request)
    {
        var result = await _deployer.DeployAsync(request.ComposeFile ?? "docker-compose.test.yml", HttpContext.RequestAborted);
        return Ok(new { status = "deployed", result });
    }

    [HttpPost("cleanup")]
    [RequireAdmin]
    public async Task<IActionResult> Cleanup([FromBody] DeployRequest request)
    {
        var result = await _deployer.CleanupAsync(request.ComposeFile ?? "docker-compose.test.yml", HttpContext.RequestAborted);
        return Ok(new { status = "cleaned", result });
    }
}

public class DeployRequest { public string? ComposeFile { get; set; } }
