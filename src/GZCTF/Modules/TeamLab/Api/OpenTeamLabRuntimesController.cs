using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Runtimes")]
[OpenApiTag("TeamLab - Runtimes", Description = "Create, inspect, reset, destroy, and access deployed TeamLab environments.")]
[Route("api/open/v1/teamlab/runtimes")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabRuntimesController(
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabRuntimeProjectionService projections,
    TeamLabRuntimeOperationApplicationService operations,
    TeamLabAuthorizationService authorization,
    TeamLabRuntimeLifecycleGuard lifecycleGuard,
    TeamLabAccessGrantService access) : ControllerBase
{
    [HttpPost]
    [OpenApiOperation("Create a runtime", "Queues deployment of a released topology for one team or automation owner.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        CreateTeamLabRuntimeModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var result = await operations.SubmitCreateAsync(actor.TokenId, actor.UserId, idempotencyKey,
            "POST:/api/open/v1/teamlab/runtimes", model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("{runtimeId:guid}")]
    [OpenApiOperation("Get a runtime", "Returns the aggregated runtime, shard, network, and asset state.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [ProducesResponseType(typeof(OpenTeamLabRuntimeModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabRuntimeModel> Get(Guid runtimeId, CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return (await runtimes.GetAsync(runtimeId, cancellationToken)).ToOpen();
    }

    [HttpPost("{runtimeId:guid}/reset")]
    [OpenApiOperation("Reset a runtime", "Queues a controlled cleanup and redeployment using the runtime release and optional overlays.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Reset(
        Guid runtimeId,
        ResetTeamLabRuntimeModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        await RequireDirectLifecycleControlAsync(runtimeId, cancellationToken);
        var result = await operations.SubmitResetAsync(actor.TokenId, actor.UserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/reset", runtimeId, model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpDelete("{runtimeId:guid}")]
    [OpenApiOperation("Destroy a runtime", "Queues cleanup of all runtime shards, assets, routes, captures, and access grants.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Destroy(
        Guid runtimeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        await RequireDirectLifecycleControlAsync(runtimeId, cancellationToken);
        var result = await operations.SubmitDestroyAsync(actor.TokenId, actor.UserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/runtimes/{runtimeId:D}", runtimeId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("{runtimeId:guid}/events")]
    [OpenApiOperation("List runtime events", "Returns cursor-paginated lifecycle and deployment events for troubleshooting and audit.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [ProducesResponseType(typeof(OpenTeamLabRuntimeEventPageModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabRuntimeEventPageModel> Events(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await projections.GetEventsAsync(runtimeId, after, limit, cancellationToken);
    }

    [HttpPost("{runtimeId:guid}/access-grants")]
    [OpenApiOperation("Create a WireGuard access grant", "Queues creation of a short-lived, single-download player access configuration.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateAccessGrant(
        Guid runtimeId,
        TeamLabAccessGrantCreateModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(model.Type, "WireGuard", StringComparison.OrdinalIgnoreCase))
            throw new TeamLabApiContractException("topology_invalid", "Only WireGuard access grants are supported.", 422);
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitAccessGrantCreateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("{runtimeId:guid}/access-grants/{grantId:guid}/download")]
    [OpenApiOperation("Download an access configuration", "Consumes the one-time download token and returns the WireGuard configuration file.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [Produces("application/x-wireguard-profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadAccessConfiguration(
        Guid runtimeId,
        Guid grantId,
        [FromQuery, Required] string token,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var result = await access.ConsumeConfigurationAsync(runtimeId, grantId, token, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(result.Configuration), "application/x-wireguard-profile", result.FileName);
    }

    [HttpDelete("{runtimeId:guid}/access-grants/{grantId:guid}")]
    [OpenApiOperation("Revoke an access grant", "Queues revocation and cleanup of an existing runtime access grant.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RevokeAccessGrant(
        Guid runtimeId,
        Guid grantId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitAccessGrantRevokeAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, grantId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task AuthorizeRuntimeAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await authorization.RequireRuntimeOwnerAsync(runtimeId, actor.UserId, User.IsInRole(nameof(Role.Admin)), cancellationToken);
    }

    private async Task RequireDirectLifecycleControlAsync(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        if (await lifecycleGuard.IsRolloutManagedAsync(runtimeId, cancellationToken))
            throw new TeamLabApiContractException(
                "runtime_managed_by_rollout",
                "This runtime is managed by a competition rollout; use the competition lifecycle API.",
                409);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);
    }
}
