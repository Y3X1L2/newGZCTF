using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Content.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/bootstrap-profiles")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenBootstrapProfilesController(
    BootstrapProfileApplicationService profiles) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.BootstrapProfilesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        BootstrapProfileCreateModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (tokenId, userId) = GetActor();
        var result = await profiles.SubmitCreateAsync(
            tokenId, new ActorContext(userId, Role.Teacher, tokenId), idempotencyKey, model, cancellationToken);
        return AcceptedOperation(result.Operation);
    }

    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.BootstrapProfilesRead)]
    [ProducesResponseType(typeof(BootstrapProfileCursorPage), StatusCodes.Status200OK)]
    public Task<BootstrapProfileCursorPage> List(
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        profiles.ListAsync(limit, after, cancellationToken);

    [HttpGet("{profileId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.BootstrapProfilesRead)]
    public async Task<IActionResult> Get(Guid profileId, CancellationToken cancellationToken)
    {
        var result = await profiles.GetAsync(profileId, cancellationToken);
        return result is null ? await NotFoundProblemAsync() : Ok(result);
    }

    [HttpPost("{profileId:guid}/versions")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.BootstrapProfilesWrite)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> PublishVersion(
        Guid profileId,
        [FromForm] BootstrapProfileVersionUploadModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (tokenId, userId) = GetActor();
        var result = await profiles.SubmitVersionAsync(
            tokenId, new ActorContext(userId, Role.Teacher, tokenId), profileId, idempotencyKey, model,
            cancellationToken);
        return AcceptedOperation(result.Operation);
    }

    [HttpGet("{profileId:guid}/versions/{version:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.BootstrapProfilesRead)]
    public async Task<IActionResult> GetVersion(
        Guid profileId,
        [FromRoute, Range(1, int.MaxValue)] int version,
        CancellationToken cancellationToken)
    {
        var result = await profiles.GetVersionAsync(profileId, version, cancellationToken);
        return result is null ? await NotFoundProblemAsync() : Ok(result);
    }

    [HttpDelete("{profileId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.BootstrapProfilesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(
        Guid profileId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (tokenId, userId) = GetActor();
        var result = await profiles.SubmitDeleteAsync(
            tokenId, new ActorContext(userId, Role.Teacher, tokenId), profileId, idempotencyKey,
            cancellationToken);
        return AcceptedOperation(result.Operation);
    }

    private IActionResult AcceptedOperation(GZCTF.Modules.Audit.Domain.ApiOperation operation)
    {
        var model = ApiOperationModel.FromEntity(operation);
        return Accepted($"/api/open/v1/operations/{model.Id}", model);
    }

    private (Guid TokenId, Guid UserId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new BootstrapProfileContractException(
            "authentication_required", "Authentication is required.", 401);
    }

    private async Task<IActionResult> NotFoundProblemAsync()
    {
        await ExternalApiProblemDetails.WriteAsync(
            HttpContext, StatusCodes.Status404NotFound,
            "bootstrap_profile_not_found", "Bootstrap profile was not found.");
        return new EmptyResult();
    }
}
