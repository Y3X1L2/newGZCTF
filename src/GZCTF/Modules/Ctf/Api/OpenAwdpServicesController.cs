using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.Ctf.Contracts;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Ctf.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/games/{gameId:int}/awdp-services")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenAwdpServicesController(
    ChallengeExternalApplicationService imports,
    IExternalChallengeCatalog catalog,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesRead)]
    public async Task<IActionResult> List(int gameId, [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] int? after = null, CancellationToken cancellationToken = default)
    {
        await AuthorizeGameAsync(gameId);
        return Ok(await catalog.ListAwdpAsync(gameId, limit, after, cancellationToken));
    }

    [HttpGet("{serviceId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesRead)]
    public async Task<IActionResult> Get(int gameId, int serviceId, CancellationToken cancellationToken)
    {
        await AuthorizeGameAsync(gameId);
        return Ok(await catalog.FindAwdpAsync(gameId, serviceId, cancellationToken)
            ?? throw new ChallengeApiContractException("awdp_service_not_found", "The AWDP service was not found.", 404));
    }

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesWrite)]
    public Task<IActionResult> ImportOne(int gameId, OpenAwdpServiceImportModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) => Import(gameId, [model], idempotencyKey,
            "POST:/api/open/v1/games/{gameId}/awdp-services", cancellationToken);

    [HttpPost("batch")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesWrite)]
    public Task<IActionResult> ImportBatch(int gameId, OpenAwdpServiceBatchImportModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) => Import(gameId, model.Items, idempotencyKey,
            "POST:/api/open/v1/games/{gameId}/awdp-services/batch", cancellationToken);

    [HttpDelete("{serviceId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesDelete)]
    public Task<IActionResult> Delete(int gameId, int serviceId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) => DeleteBatch(gameId, [serviceId], idempotencyKey,
            "DELETE:/api/open/v1/games/{gameId}/awdp-services/{serviceId}", cancellationToken);

    [HttpPost("batch-delete")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesDelete)]
    public async Task<IActionResult> DeleteBatch(int gameId, [FromBody] IReadOnlyList<int> serviceIds,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        string? routeKey = null, CancellationToken cancellationToken = default)
    {
        await AuthorizeGameAsync(gameId);
        var (tokenId, actorUserId) = GetActor();
        var result = await imports.SubmitAwdpDeleteAsync(gameId, tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId), idempotencyKey, serviceIds,
            routeKey ?? $"POST:/api/open/v1/games/{gameId}/awdp-services/batch-delete", cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task<IActionResult> Import(int gameId, IReadOnlyList<OpenAwdpServiceImportModel> items,
        string idempotencyKey, string routeKey, CancellationToken cancellationToken)
    {
        await AuthorizeGameAsync(gameId);
        var (tokenId, actorUserId) = GetActor();
        var result = await imports.SubmitAwdpImportAsync(gameId, tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId), idempotencyKey, items, routeKey.Replace("{gameId}", gameId.ToString()), cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task AuthorizeGameAsync(int gameId)
    {
        var result = await authorization.AuthorizeAsync(User, null,
            new ApiResourceRequirement("game", gameId.ToString(), true));
        if (!result.Succeeded)
            throw new ChallengeApiContractException("insufficient_permission", $"The token does not grant access to game {gameId}.", 403);
    }

    private (Guid TokenId, Guid ActorUserId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return (tokenId, actorUserId);
        throw new ChallengeApiContractException("authentication_required", "Authentication is required.", 401);
    }
}
