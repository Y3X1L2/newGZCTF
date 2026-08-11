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
[Route("api/open/v1/teams")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class TeamsOpenApiController(
    AcademicImportApplicationService imports,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpPost("import")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamsWrite, Roles = nameof(Role.Admin))]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Import(
        [FromBody] TeamImportBatchModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var grant = await authorization.AuthorizeAsync(
            User, null, new ApiResourceRequirement("team", "*", true));
        if (!grant.Succeeded)
            throw new AcademicImportApiContractException(
                "insufficient_permission", "The token does not grant access to team:*.", 403);
        var (tokenId, actorUserId) = GetActor();
        var result = await imports.SubmitTeamsAsync(
            tokenId, actorUserId, idempotencyKey, model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    (Guid TokenId, Guid ActorUserId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return (tokenId, actorUserId);
        throw new AcademicImportApiContractException("authentication_required", "Authentication is required.", 401);
    }
}
