using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/teamlab/runtimes/{runtimeId:guid}")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenTeamLabTrafficController(
    TeamLabTrafficApplicationService traffic,
    TeamLabAuthorizationService authorization) : ControllerBase
{
    [HttpGet("traffic/flows")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTrafficRead)]
    public async Task<TeamLabTrafficFlowPageModel> GetFlows(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery, Range(1, 200)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.GetFlowsAsync(runtimeId, after, limit, cancellationToken);
    }

    [HttpPost("captures")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureWrite)]
    public async Task<ActionResult<TeamLabCaptureModel>> StartCapture(
        Guid runtimeId,
        CreateTeamLabCaptureModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var capture = await traffic.StartCaptureAsync(runtimeId, model, idempotencyKey, cancellationToken);
        return Created($"/api/open/v1/teamlab/runtimes/{runtimeId:D}/captures/{capture.Id:D}", capture);
    }

    [HttpGet("captures/{captureId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureRead)]
    public async Task<TeamLabCaptureModel> GetCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.GetCaptureAsync(runtimeId, captureId, cancellationToken);
    }

    [HttpPost("captures/{captureId:guid}/stop")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureWrite)]
    public async Task<TeamLabCaptureModel> StopCapture(
        Guid runtimeId,
        Guid captureId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = idempotencyKey;
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.StopCaptureAsync(runtimeId, captureId, cancellationToken);
    }

    [HttpGet("captures/{captureId:guid}/download")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureRead)]
    public async Task<IActionResult> DownloadCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var download = await traffic.DownloadCaptureAsync(runtimeId, captureId, cancellationToken);
        if (!download.Success || download.Stream is null)
        {
            download.Owner?.Dispose();
            throw new TeamLabApiContractException("capture_not_found", download.Message, 404);
        }

        HttpContext.Response.RegisterForDispose(download.Owner ?? download.Stream);
        return File(download.Stream, download.ContentType, download.FileName, enableRangeProcessing: true);
    }

    private async Task AuthorizeRuntimeAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);
        await authorization.RequireRuntimeOwnerAsync(
            runtimeId, userId, User.IsInRole(nameof(Role.Admin)), cancellationToken);
    }
}
