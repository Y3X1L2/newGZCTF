using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/containers")]
public class ContainerController : ControllerBase
{
    private readonly DockerService _docker;

    public ContainerController(DockerService docker) { _docker = docker; }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateContainerRequest request, CancellationToken token)
    {
        var result = await _docker.CreateContainerAsync(request, token);
        if (result is null) return StatusCode(500, new { message = "Container creation failed" });
        return Ok(result);
    }

    [HttpDelete("{containerId}")]
    public async Task<IActionResult> Destroy(string containerId, CancellationToken token)
    {
        await _docker.DestroyContainerAsync(containerId, token);
        return NoContent();
    }

    [HttpDelete("networks/{networkName}")]
    public async Task<IActionResult> RemoveNetwork(string networkName, CancellationToken token)
    {
        await _docker.RemoveNetworkAsync(networkName, token);
        return NoContent();
    }
}
