using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/containers")]
public class ContainerController : ControllerBase
{
    private readonly DockerService _docker;
    private readonly AgentOperationGate _gate;

    public ContainerController(DockerService docker, AgentOperationGate gate)
    {
        _docker = docker;
        _gate = gate;
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateContainerRequest request, CancellationToken token)
    {
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.DockerCreate, token);
        var result = await _docker.CreateContainerAsync(request, token);
        return result is null
            ? throw new AgentOperationException(
                "Docker", "docker.operation_failed", "Container creation failed.", true)
            : Ok(result);
    }

    [HttpDelete("{containerId}")]
    public async Task<IActionResult> Destroy(string containerId, CancellationToken token)
    {
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.Control, token);
        await _docker.DestroyContainerAsync(containerId, token);
        return NoContent();
    }

    [HttpPost("{containerId}/exec")]
    public async Task<IActionResult> Execute(string containerId, [FromBody] ExecuteContainerCommandRequest request,
        CancellationToken token)
    {
        if (request.Command.Count == 0)
            throw new ArgumentException("Command is empty.", nameof(request));

        var timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 60));
        var result = await _docker.ExecuteContainerCommandAsync(containerId, request.Command, timeout, token);
        return Ok(result);
    }

    [HttpDelete("networks/{networkName}")]
    public async Task<IActionResult> RemoveNetwork(string networkName, CancellationToken token)
    {
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        await _docker.RemoveNetworkAsync(networkName, token);
        return NoContent();
    }

    [HttpPost("fabric/networks")]
    public async Task<IActionResult> CreateFabricNetwork([FromBody] FabricNetworkRequest request,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.NetworkName) || string.IsNullOrWhiteSpace(request.Cidr))
            throw new ArgumentException("Network name and CIDR are required.", nameof(request));

        await using var permit = await _gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var result = await _docker.CreateFabricNetworkAsync(request.NetworkName, request.Cidr, token);
        return Ok(result);
    }

    [HttpPost("{containerId}/fabric/interfaces")]
    public async Task<IActionResult> AttachFabricInterface(string containerId, [FromBody] FabricAttachRequest request,
        CancellationToken token)
    {
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var result = await _docker.AttachFabricInterfaceAsync(containerId, request, token);
        return Ok(result);
    }

    [HttpPost("{containerId}/fabric/forwarding")]
    public async Task<IActionResult> EnableFabricForwarding(string containerId, CancellationToken token)
    {
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var result = await _docker.EnableFabricForwardingAsync(containerId, token);
        return Ok(result);
    }

    [HttpPost("{containerId}/fabric/routes")]
    public async Task<IActionResult> ApplyFabricRoute(string containerId, [FromBody] FabricRouteRequest request,
        CancellationToken token)
    {
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var result = await _docker.ApplyFabricRouteAsync(containerId, request.TargetCidr, request.GatewayIp, token);
        return Ok(result);
    }

    [HttpPost("{containerId}/fabric/probe")]
    public async Task<IActionResult> ProbeFabric(string containerId, [FromBody] FabricProbeRequest request,
        CancellationToken token)
    {
        var result = await _docker.ProbeFabricAsync(containerId, request.TargetIp, token);
        return Ok(result);
    }

    [HttpDelete("fabric/networks/{networkName}")]
    public async Task<IActionResult> RemoveFabricNetwork(string networkName, CancellationToken token)
    {
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var result = await _docker.RemoveFabricNetworkAsync(networkName, token);
        return Ok(result);
    }
}
