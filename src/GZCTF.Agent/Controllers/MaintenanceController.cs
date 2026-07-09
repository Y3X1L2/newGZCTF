using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/maintenance")]
public class MaintenanceController(AgentMaintenanceService service) : ControllerBase
{
    [HttpPost("sync-agent")]
    public async Task<IActionResult> SyncAgent([FromBody] AgentSyncRequest request, CancellationToken token)
    {
        var result = await service.SyncAgentAsync(request, token);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
