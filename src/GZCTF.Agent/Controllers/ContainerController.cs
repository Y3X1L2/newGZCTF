using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/containers")]
public class ContainerController : ControllerBase
{
    private readonly DockerService _docker;
    private readonly HostNetworkPolicyService _networkPolicy;
    private readonly ILogger<ContainerController> _logger;

    public ContainerController(DockerService docker, HostNetworkPolicyService networkPolicy,
        ILogger<ContainerController> logger)
    {
        _docker = docker;
        _networkPolicy = networkPolicy;
        _logger = logger;
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateContainerRequest request, CancellationToken token)
    {
        try
        {
            var result = await _docker.CreateContainerAsync(request, token);
            if (result is null) return StatusCode(500, new { message = "Container creation failed" });
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent container creation failed for image {Image}", request.Image);
            return StatusCode(500, new { message = ex.Message, type = ex.GetType().Name });
        }
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

    [HttpPost("policies/apply")]
    public async Task<IActionResult> ApplyPolicy([FromBody] NetworkPolicySetRequest request, CancellationToken token)
    {
        var result = await _networkPolicy.ApplyAsync(request, token);
        return result.Success ? Ok(new { message = result.Message }) : StatusCode(500, new { message = result.Message });
    }

    [HttpDelete("policies/{setName}")]
    public async Task<IActionResult> RemovePolicy(string setName, CancellationToken token)
    {
        var result = await _networkPolicy.RemoveAsync(setName, token);
        return result.Success ? NoContent() : StatusCode(500, new { message = result.Message });
    }
}
