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
[OpenApiTags("TeamLab - Link policies")]
[Route("api/open/v1/teamlab/link-policies")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabLinkPoliciesController(
    TeamLabLinkPolicyService policies,
    TeamLabScopeAuthorizationService scopeAuthorization) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabLinkPoliciesWrite)]
    [OpenApiOperation("应用链路策略", "在运行时网段/链路上声明式应用损伤或访问策略；同参数重复应用幂等，不同参数需先恢复")]
    [ProducesResponseType(typeof(TeamLabLinkPolicyModel), StatusCodes.Status201Created)]
    public async Task<IActionResult> Apply(
        ApplyTeamLabLinkPolicyModel model,
        CancellationToken cancellationToken)
    {
        await scopeAuthorization.RequireRuntimeScopeAsync(
            model.RuntimeId, Actor().TokenId, IsAdministrator(), writable: true, cancellationToken);
        var policy = await policies.ApplyAsync(model, cancellationToken);
        return Created($"/api/open/v1/teamlab/link-policies/{policy.Id:D}", policy);
    }

    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabLinkPoliciesRead)]
    [OpenApiOperation("列出运行时链路策略", "默认返回未恢复的策略，可按 active/recovered/failed 过滤")]
    [ProducesResponseType(typeof(TeamLabLinkPolicyPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabLinkPolicyPageModel> List(
        [FromQuery, Required] Guid runtimeId,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, Actor().TokenId, IsAdministrator(), writable: false, cancellationToken);
        return await policies.ListByRuntimeAsync(runtimeId, status, after, limit, cancellationToken);
    }

    [HttpPost("{policyId:guid}/recover")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabLinkPoliciesWrite)]
    [OpenApiOperation("恢复链路策略", "手工恢复一条活动或失败的链路策略；已恢复的策略幂等返回")]
    [ProducesResponseType(typeof(TeamLabLinkPolicyModel), StatusCodes.Status200OK)]
    public async Task<TeamLabLinkPolicyModel> Recover(Guid policyId, CancellationToken cancellationToken)
    {
        await scopeAuthorization.RequireLinkPolicyScopeAsync(
            policyId, Actor().TokenId, IsAdministrator(), writable: true, cancellationToken);
        return await policies.RecoverAsync(policyId, cancellationToken);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份验证", 401);
    }

    private bool IsAdministrator() => User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
        ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
        type == "teamlab-scope" && id == "*");
}
