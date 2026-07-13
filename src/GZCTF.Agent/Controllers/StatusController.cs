using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/status")]
public class StatusController(Services.AgentCapabilityService capabilities) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
        => Ok(await capabilities.GetManifestAsync(await capabilities.GetBinarySha256Async(), token));
}
