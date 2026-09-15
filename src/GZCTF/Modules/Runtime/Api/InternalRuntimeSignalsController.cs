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
    [HttpPost("~/api/internal/teamlab/runtime-signals/batch")]
    [AllowAnonymous]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    public async Task<IActionResult> IngestBatch(
        AgentRuntimeSignalBatchModel model,
        CancellationToken cancellationToken)
    {
        return await IngestCoreAsync(model.NodeId, model.Signals, cancellationToken);
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    public async Task<IActionResult> Ingest(
        Guid nodeId,
        AgentRuntimeSignalModel model,
        CancellationToken cancellationToken)
    {
        return await IngestCoreAsync(nodeId, [model], cancellationToken, single: true);
    }

    private async Task<IActionResult> IngestCoreAsync(
        Guid nodeId,
        IReadOnlyList<AgentRuntimeSignalModel> models,
        CancellationToken cancellationToken,
        bool single = false)
    {
        var bearer = Request.Headers.Authorization.ToString();
        var token = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? bearer[7..].Trim()
            : string.Empty;

        try
        {
            var result = await signals.IngestBatchAuthenticatedAsync(nodeId, token, models, cancellationToken);
            return Ok(single ? result[0] : result);
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
