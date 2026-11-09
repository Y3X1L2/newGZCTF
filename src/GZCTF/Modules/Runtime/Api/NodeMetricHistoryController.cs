using System.ComponentModel.DataAnnotations;
using GZCTF.Middlewares;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Runtime.Api;

[ApiController]
[RequireAdmin]
[Route("api/v1/nodes/metrics/history")]
public sealed class NodeMetricHistoryController(NodeMetricHistoryQuery history) : ControllerBase
{
    /// <summary>Get persisted platform or single-node load history.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NodeMetricHistory), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NodeMetricHistory>> GetHistory(
        [FromQuery, RegularExpression("^(1h|24h|7d)$")] string range = "24h",
        [FromQuery] Guid? nodeId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await history.ReadAsync(range, nodeId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
