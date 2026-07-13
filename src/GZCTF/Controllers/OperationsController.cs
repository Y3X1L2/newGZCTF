using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Middlewares;
using GZCTF.Models.Request;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/admin/operations")]
[Produces(MediaTypeNames.Application.Json)]
[RequireAdmin]
public sealed class OperationsController(OperationalEventQueryService events) : ControllerBase
{
    [HttpGet("events")]
    [ProducesResponseType(typeof(OperationalEventViewPageModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Events(
        [FromQuery] OperationalEventQueryModel query,
        CancellationToken token)
    {
        try
        {
            return Ok(await events.QueryAsync(query, token));
        }
        catch (Exception exception) when (exception is InvalidTimeCursorException or ArgumentException)
        {
            return BadRequest(new RequestResponse("invalid_query", StatusCodes.Status400BadRequest));
        }
    }

    [HttpGet("recovery")]
    [ProducesResponseType(typeof(OperationalEventViewPageModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Recovery(
        [FromQuery] OperationalEventQueryModel query,
        CancellationToken token)
    {
        try
        {
            return Ok(await events.QueryRecoveryAsync(query, token));
        }
        catch (Exception exception) when (exception is InvalidTimeCursorException or ArgumentException)
        {
            return BadRequest(new RequestResponse("invalid_query", StatusCodes.Status400BadRequest));
        }
    }

    [HttpGet("correlations/{correlationId:guid}")]
    [ProducesResponseType(typeof(OperationalCorrelationSummaryModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Correlation(Guid correlationId, CancellationToken token)
    {
        var result = await events.GetCorrelationAsync(correlationId, token);
        return result is null ? NotFound() : Ok(result);
    }
}
