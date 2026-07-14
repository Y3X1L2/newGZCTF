using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/teamlab/runtimes/{runtimeId:guid}")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabTrafficController(
    TeamLabTrafficApplicationService traffic,
    TeamLabAuthorizationService authorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet("traffic/flows")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTrafficRead)]
    [ProducesResponseType(typeof(TeamLabTrafficFlowPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabTrafficFlowPageModel> GetFlows(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.GetFlowsAsync(runtimeId, after, limit, cancellationToken);
    }

    [HttpPost("captures")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartCapture(
        Guid runtimeId,
        CreateTeamLabCaptureModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitCaptureStartAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, model, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("captures/{captureId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureRead)]
    [ProducesResponseType(typeof(OpenTeamLabCaptureModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabCaptureModel> GetCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return (await traffic.GetCaptureAsync(runtimeId, captureId, cancellationToken)).ToOpen();
    }

    [HttpPost("captures/{captureId:guid}/stop")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StopCapture(
        Guid runtimeId,
        Guid captureId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitCaptureStopAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, captureId, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("captures/{captureId:guid}/download")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureRead)]
    [Produces("application/vnd.tcpdump.pcap")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
            throw new TeamLabApiContractException(
                "capture_not_found", "The capture is unavailable or expired.", 404);
        }

        HttpContext.Response.RegisterForDispose(download.Owner ?? download.Stream);
        return File(download.Stream, download.ContentType, download.FileName, enableRangeProcessing: true);
    }

    private async Task AuthorizeRuntimeAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await authorization.RequireRuntimeOwnerAsync(
            runtimeId, actor.UserId, User.IsInRole(nameof(Role.Admin)), cancellationToken);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);
    }

    private AcceptedResult AcceptedOperation(GZCTF.Modules.Audit.Application.IdempotencyBeginResult result)
    {
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }
}
