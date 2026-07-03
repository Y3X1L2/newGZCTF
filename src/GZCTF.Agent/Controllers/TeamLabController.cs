using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab")]
public class TeamLabController(TeamLabNetworkService service) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken token) => Ok(await service.GetStatusAsync(token));

    [HttpPost("bridges")]
    public async Task<IActionResult> CreateBridge([FromBody] TeamLabBridgeRequest request, CancellationToken token) =>
        Ok(await service.CreateBridgeAsync(request, token));

    [HttpPost("routers")]
    public async Task<IActionResult> CreateRouter([FromBody] TeamLabRouterRequest request, CancellationToken token) =>
        Ok(await service.CreateRouterAsync(request, token));

    [HttpPost("wireguard")]
    public async Task<IActionResult> ConfigureWireGuard([FromBody] TeamLabWireGuardRequest request, CancellationToken token) =>
        Ok(await service.ConfigureWireGuardAsync(request, token));

    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup([FromBody] TeamLabCleanupRequest request, CancellationToken token) =>
        Ok(await service.CleanupAsync(request, token));
}
