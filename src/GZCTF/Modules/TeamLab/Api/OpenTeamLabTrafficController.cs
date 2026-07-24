using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Traffic and Captures")]
[OpenApiTag("TeamLab - Traffic and Captures", Description = "Query traffic flows and paths, and manage bounded packet captures for a TeamLab runtime.")]
[Route("api/open/v1/teamlab/runtimes/{runtimeId:guid}")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabTrafficController(
    TeamLabTrafficApplicationService traffic,
    TeamLabCaptureArtifactStore captureArtifacts,
    TeamLabAuthorizationService authorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet("traffic/flows")]
    [OpenApiOperation("List traffic flows", "Returns cursor-paginated, runtime-scoped flow metadata collected from the TeamLab data plane.")]
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

    [HttpGet("traffic/paths")]
    [OpenApiOperation("List correlated traffic paths", "Returns end-to-end traffic path correlations across participating assets and network segments.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTrafficRead)]
    [ProducesResponseType(typeof(TeamLabTrafficPathPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabTrafficPathPageModel> GetPaths(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.GetPathsAsync(runtimeId, after, limit, cancellationToken);
    }

    [HttpGet("traffic/paths/{pathId:guid}")]
    [OpenApiOperation("Get a traffic path", "Returns the ordered hops and evidence for one correlated traffic path.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTrafficRead)]
    [ProducesResponseType(typeof(TeamLabTrafficPathModel), StatusCodes.Status200OK)]
    public async Task<TeamLabTrafficPathModel> GetPath(
        Guid runtimeId,
        Guid pathId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.GetPathAsync(runtimeId, pathId, cancellationToken);
    }

    [HttpPost("captures")]
    [OpenApiOperation("Start a packet capture", "Queues a bounded packet capture for selected runtime shards or network segments.")]
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
    [OpenApiOperation("Get packet capture status", "Returns capture scope, limits, progress, artifact state, and retention metadata.")]
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
    [OpenApiOperation("Stop a packet capture", "Queues an early stop and finalization of a running packet capture.")]
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
    [OpenApiOperation("Download a packet capture", "Streams the finalized runtime capture archive.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureRead)]
    [Produces("application/x-tar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var descriptor = await traffic.DownloadCaptureAsync(runtimeId, captureId, cancellationToken);
        Response.ContentType = "application/x-tar";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{descriptor.FileName}\"";
        try
        {
            await captureArtifacts.WriteArchiveAsync(descriptor, Response.Body, cancellationToken);
            return new EmptyResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HttpContext.Abort();
            return new EmptyResult();
        }
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
