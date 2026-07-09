using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/status")]
public class StatusController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        string? binarySha256 = null;
        const string installedPath = "/usr/local/bin/gzctf-agent";
        if (System.IO.File.Exists(installedPath))
            binarySha256 = await Services.AgentMaintenanceService.ComputeFileSha256Async(installedPath, token);

        return Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            agentVersion = typeof(StatusController).Assembly.GetName().Version?.ToString(),
            protocolVersion = 3,
            binarySha256
        });
    }
}
