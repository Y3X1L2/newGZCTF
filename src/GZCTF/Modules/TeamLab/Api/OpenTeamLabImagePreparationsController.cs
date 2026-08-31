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
[OpenApiTags("TeamLab - Image Preparation")]
[OpenApiTag("TeamLab - Image Preparation", Description = "Inspect and request release-scoped image preparation without querying node inventory.")]
[Route("api/open/v1/teamlab/preparations")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabImagePreparationsController(
    TeamLabReleaseImagePreparationService preparation,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet("releases/{releaseId:guid}")]
    [OpenApiOperation("获取镜像准备状态", "返回发布版本的就绪投影：planAvailable/preparing/readyToStart/blocked 与按模板统计的节点就绪计数。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [ProducesResponseType(typeof(TeamLabReleasePreparationModel), StatusCodes.Status200OK)]
    public async Task<TeamLabReleasePreparationModel> Get(Guid releaseId, CancellationToken cancellationToken)
    {
        await RequireScopeAsync(releaseId, false, cancellationToken);
        return await preparation.GetPreparationAsync(releaseId, cancellationToken);
    }

    [HttpPost("releases/{releaseId:guid}")]
    [OpenApiOperation("提交镜像准备", "幂等提交发布版本的镜像预分发，适用于发布后或失败后的显式重试。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Queue(
        Guid releaseId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await RequireScopeAsync(releaseId, true, cancellationToken);
        var result = await operations.SubmitReleasePreparationAsync(
            actor.TokenId, actor.UserId, idempotencyKey, releaseId, scopeId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpDelete("releases/{releaseId:guid}")]
    [OpenApiOperation("释放镜像准备引用", "幂等释放发布版本的镜像预分发引用，停止继续保留准备状态。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Release(
        Guid releaseId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await RequireScopeAsync(releaseId, true, cancellationToken);
        var result = await operations.SubmitReleasePreparationReleaseAsync(
            actor.TokenId, actor.UserId, idempotencyKey, releaseId, scopeId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task<Guid> RequireScopeAsync(
        Guid releaseId,
        bool writable,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        return await scopeAuthorization.RequireReleaseScopeAsync(
            releaseId, actor.TokenId, IsAdministrator(), writable, cancellationToken);
    }

    private bool IsAdministrator() => User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
        ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
        type == "teamlab-scope" && id == "*");

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
    }
}
