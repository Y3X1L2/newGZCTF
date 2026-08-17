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
[OpenApiTags("TeamLab - Connectors")]
[Route("api/open/v1/teamlab/connectors")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabConnectorsController(
    TeamLabConnectorService connectors,
    TeamLabScopeAuthorizationService scopeAuthorization) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsRead)]
    [OpenApiOperation("列出现场连接器", "列出平台级与已授权 control scope 的连接器及占用状态，不暴露接入地址")]
    [ProducesResponseType(typeof(TeamLabConnectorPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabConnectorPageModel> List(
        [FromQuery] Guid? scopeId,
        [FromQuery] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        if (scopeId is { } resolved)
            await scopeAuthorization.RequireReadableAsync(resolved, Actor().TokenId, IsAdministrator(), cancellationToken);
        return await connectors.ListAsync(scopeId, after, limit, cancellationToken);
    }

    [HttpGet("{connectorId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsRead)]
    [OpenApiOperation("获取现场连接器", "返回类型、授权范围、容量、健康与当前占用")]
    [ProducesResponseType(typeof(TeamLabConnectorModel), StatusCodes.Status200OK)]
    public async Task<TeamLabConnectorModel> Get(
        Guid connectorId,
        [FromQuery] Guid? scopeId,
        CancellationToken cancellationToken)
    {
        if (scopeId is { } resolved)
            await scopeAuthorization.RequireReadableAsync(resolved, Actor().TokenId, IsAdministrator(), cancellationToken);
        return await connectors.GetAsync(connectorId, scopeId, cancellationToken);
    }

    [HttpPost("{connectorId:guid}/leases")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsWrite)]
    [OpenApiOperation("占用现场连接器", "为运行时申请连接器租约；独占连接器同一时间只属于一个运行时，重复申请幂等返回")]
    [ProducesResponseType(typeof(TeamLabConnectorLeaseModel), StatusCodes.Status201Created)]
    public async Task<IActionResult> Acquire(
        Guid connectorId,
        AcquireTeamLabConnectorLeaseModel model,
        CancellationToken cancellationToken)
    {
        var runtimeScopeId = await scopeAuthorization.RequireRuntimeScopeAsync(
            model.RuntimeId, Actor().TokenId, IsAdministrator(), writable: true, cancellationToken);
        var lease = await connectors.AcquireAsync(connectorId, model.RuntimeId, runtimeScopeId, cancellationToken);
        return Created($"/api/open/v1/teamlab/connectors/{connectorId:D}", lease);
    }

    [HttpPost("{connectorId:guid}/leases/release")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsWrite)]
    [OpenApiOperation("释放现场连接器", "释放该运行时的活动租约；重复释放幂等返回")]
    [ProducesResponseType(typeof(TeamLabConnectorLeaseModel), StatusCodes.Status200OK)]
    public async Task<TeamLabConnectorLeaseModel> Release(
        Guid connectorId,
        ReleaseTeamLabConnectorLeaseModel model,
        CancellationToken cancellationToken)
    {
        await scopeAuthorization.RequireRuntimeScopeAsync(
            model.RuntimeId, Actor().TokenId, IsAdministrator(), writable: true, cancellationToken);
        return await connectors.ReleaseAsync(
            connectorId, model.RuntimeId, Domain.TeamLabConnectorLeaseReleaseReason.ManualRelease, cancellationToken);
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
