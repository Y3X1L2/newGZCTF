using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/teamlab/runtimes")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenTeamLabRuntimesController(
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabRuntimeProjectionService projections,
    TeamLabRuntimeOperationApplicationService operations,
    TeamLabAuthorizationService authorization,
    TeamLabAccessGrantService access) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
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
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    public async Task<TeamLabRuntimeProjectionModel> Get(Guid runtimeId, CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await runtimes.GetAsync(runtimeId, cancellationToken);
    }

    [HttpPost("{runtimeId:guid}/reset")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    public async Task<IActionResult> Reset(
        Guid runtimeId,
        ResetTeamLabRuntimeModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitResetAsync(actor.TokenId, actor.UserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/reset", runtimeId, model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpDelete("{runtimeId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    public async Task<IActionResult> Destroy(
        Guid runtimeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitDestroyAsync(actor.TokenId, actor.UserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/runtimes/{runtimeId:D}", runtimeId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("{runtimeId:guid}/events")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    public async Task<IReadOnlyList<TeamLabRuntimeEventModel>> Events(
        Guid runtimeId,
        [FromQuery] long after = 0,
        [FromQuery, Range(1, 200)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await projections.GetEventsAsync(runtimeId, after, limit, cancellationToken);
    }

    [HttpPost("{runtimeId:guid}/access-grants")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    public async Task<ActionResult<TeamLabAccessGrantModel>> CreateAccessGrant(
        Guid runtimeId,
        TeamLabAccessGrantCreateModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = idempotencyKey;
        if (!string.Equals(model.Type, "WireGuard", StringComparison.OrdinalIgnoreCase))
            throw new TeamLabApiContractException("topology_invalid", "Only WireGuard access grants are supported.", 422);
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var grant = await access.CreateAsync(runtimeId, cancellationToken);
        return Created($"/api/open/v1/teamlab/runtimes/{runtimeId:D}/access-grants/{grant.Id:D}", grant);
    }

    [HttpGet("{runtimeId:guid}/access-grants/{grantId:guid}/download")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
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
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    public async Task<IActionResult> RevokeAccessGrant(Guid runtimeId, Guid grantId, CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        await access.RevokeAsync(runtimeId, grantId, cancellationToken);
        return NoContent();
    }

    private async Task AuthorizeRuntimeAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await authorization.RequireRuntimeOwnerAsync(runtimeId, actor.UserId, User.IsInRole(nameof(Role.Admin)), cancellationToken);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);
    }
}
