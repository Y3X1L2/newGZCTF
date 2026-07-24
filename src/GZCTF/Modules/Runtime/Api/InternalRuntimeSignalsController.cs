using GZCTF.Middlewares;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GZCTF.Modules.Runtime.Api;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/nodes/{nodeId:guid}/runtime-signals")]
public sealed class InternalRuntimeSignalsController(RuntimeSignalService signals) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    public async Task<IActionResult> Ingest(
        Guid nodeId,
        AgentRuntimeSignalModel model,
        CancellationToken cancellationToken)
    {
        var bearer = Request.Headers.Authorization.ToString();
        var token = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? bearer[7..].Trim()
            : string.Empty;

        try
        {
            return Ok(await signals.IngestAuthenticatedAsync(nodeId, token, model, cancellationToken));
        }
        catch (RuntimeSignalNodeNotFoundException)
        {
            return NotFound();
        }
        catch (RuntimeSignalAuthenticationException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (RuntimeSignalConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

}
