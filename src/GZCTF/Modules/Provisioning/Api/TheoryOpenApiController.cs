using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Provisioning.Application;
using GZCTF.Modules.Provisioning.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Provisioning.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/theory")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class TheoryOpenApiController(
    AcademicImportApplicationService imports,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpPost("questions/import")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TheoryWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ImportQuestions(
        [FromBody] TheoryQuestionImportBatchModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await RequireResourceAsync("theory-bank", "*");
        var (tokenId, actorUserId) = GetActor();
        var result = await imports.SubmitTheoryQuestionsAsync(
            tokenId, actorUserId, idempotencyKey, model, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpPut("games/{gameId:int}/paper")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TheoryWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ImportPaper(
        int gameId,
        [FromBody] TheoryPaperImportModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await RequireResourceAsync("game", gameId.ToString());
        var (tokenId, actorUserId) = GetActor();
        var result = await imports.SubmitTheoryPaperAsync(
            gameId, tokenId, actorUserId, idempotencyKey, model, cancellationToken);
        return AcceptedOperation(result);
    }

    AcceptedResult AcceptedOperation(IdempotencyBeginResult result)
    {
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    async Task RequireResourceAsync(string resourceType, string resourceId)
    {
        var result = await authorization.AuthorizeAsync(
            User, null, new ApiResourceRequirement(resourceType, resourceId, true));
        if (!result.Succeeded)
            throw new AcademicImportApiContractException(
                "insufficient_permission", $"The token does not grant access to {resourceType}:{resourceId}.", 403);
    }

    (Guid TokenId, Guid ActorUserId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return (tokenId, actorUserId);
        throw new AcademicImportApiContractException("authentication_required", "Authentication is required.", 401);
    }
}
