using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.Ctf.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Ctf.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/games/{gameId:int}/challenges")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenChallengesController(
    ChallengeExternalApplicationService challenges,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesRead)]
    [ProducesResponseType(typeof(OpenChallengePageModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        int gameId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeGameAsync(gameId);
        return Ok(await challenges.ListAsync(gameId, limit, after, cancellationToken));
    }

    [HttpGet("{challengeId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesRead)]
    [ProducesResponseType(typeof(OpenChallengeModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int gameId,
        int challengeId,
        CancellationToken cancellationToken)
    {
        await AuthorizeGameAsync(gameId);
        return Ok(await challenges.GetAsync(gameId, challengeId, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ImportOne(
        int gameId,
        OpenChallengeImportModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeGameAsync(gameId);
        var (tokenId, actorUserId) = GetActor();
        var result = await challenges.SubmitImportAsync(
            gameId,
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            [model],
            $"POST:/api/open/v1/games/{gameId}/challenges",
            cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("batch")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ImportBatch(
        int gameId,
        OpenChallengeBatchImportModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeGameAsync(gameId);
        var (tokenId, actorUserId) = GetActor();
        var result = await challenges.SubmitImportAsync(
            gameId,
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            model.Items,
            $"POST:/api/open/v1/games/{gameId}/challenges/batch",
            cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpDelete("{challengeId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesDelete)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(
        int gameId,
        int challengeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeGameAsync(gameId);
        var (tokenId, actorUserId) = GetActor();
        var result = await challenges.SubmitDeleteAsync(
            gameId,
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            [challengeId],
            $"DELETE:/api/open/v1/games/{gameId}/challenges/{challengeId}",
            cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("batch-delete")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ChallengesDelete)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> DeleteBatch(
        int gameId,
        OpenChallengeBatchDeleteModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeGameAsync(gameId);
        var (tokenId, actorUserId) = GetActor();
        var result = await challenges.SubmitDeleteAsync(
            gameId,
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            model.ChallengeIds,
            $"POST:/api/open/v1/games/{gameId}/challenges/batch-delete",
            cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task AuthorizeGameAsync(int gameId)
    {
        var result = await authorization.AuthorizeAsync(
            User,
            null,
            new ApiResourceRequirement("game", gameId.ToString(), true));
        if (!result.Succeeded)
            throw new ChallengeApiContractException(
                "insufficient_permission",
                $"The token does not grant access to game {gameId}.",
                403);
    }

    private (Guid TokenId, Guid ActorUserId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return (tokenId, actorUserId);
        throw new ChallengeApiContractException(
            "authentication_required", "Authentication is required.", 401);
    }
}
