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

    [HttpGet("nodes/{nodeId:guid}/interfaces")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsWrite)]
    [OpenApiOperation("列出连接器节点网卡", "按需读取受管节点当前网卡，用于登记受管网卡连接器")]
    [ProducesResponseType(typeof(IReadOnlyList<GZCTF.TeamLab.Contracts.TeamLabHostInterface>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<GZCTF.TeamLab.Contracts.TeamLabHostInterface>> ListNodeInterfaces(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        RequireWildcardGrant("节点网卡查询需要 teamlab-scope:* 资源授权");
        return await connectors.GetNodeInterfacesAsync(nodeId, cancellationToken);
    }

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsWrite)]
    [OpenApiOperation("登记现场连接器", "登记平台级或当前 token 获授权 control scope 内的现场连接器")]
    [ProducesResponseType(typeof(TeamLabConnectorModel), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(
        OpenRegisterTeamLabConnectorModel model,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await RequireTargetScopeAsync(model.ControlScopeId, actor.TokenId, cancellationToken);
        var connector = await connectors.RegisterAsync(model.ToInternal(), cancellationToken);
        return Created($"/api/open/v1/teamlab/connectors/{connector.Id:D}", connector);
    }

    [HttpPut("{connectorId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsWrite)]
    [OpenApiOperation("修改现场连接器", "替换连接器登记信息；有活动租约时拒绝修改")]
    [ProducesResponseType(typeof(TeamLabConnectorModel), StatusCodes.Status200OK)]
    public async Task<TeamLabConnectorModel> Update(
        Guid connectorId,
        OpenUpdateTeamLabConnectorModel model,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var currentScopeId = await scopeAuthorization.RequireConnectorScopeAsync(
            connectorId, actor.TokenId, IsAdministrator(), writable: true, cancellationToken);
        await RequireTargetScopeAsync(model.ControlScopeId, actor.TokenId, cancellationToken);
        return await connectors.UpdateAsync(connectorId, currentScopeId, model.ToInternal(), cancellationToken);
    }

    [HttpGet("{connectorId:guid}/health")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsRead)]
    [OpenApiOperation("查询现场连接器健康", "按需探测受管网卡并返回当前健康状态和观测时间")]
    [ProducesResponseType(typeof(OpenTeamLabConnectorHealthModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabConnectorHealthModel> Health(
        Guid connectorId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await scopeAuthorization.RequireConnectorScopeAsync(
            connectorId, actor.TokenId, IsAdministrator(), writable: false, cancellationToken);
        var connector = await connectors.GetAsync(connectorId, scopeId, cancellationToken);
        return new OpenTeamLabConnectorHealthModel(connector.Id, connector.Health, connector.HealthObservedAt);
    }

    [HttpPost("{connectorId:guid}/archive")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabConnectorsWrite)]
    [OpenApiOperation("归档现场连接器", "无活动租约时归档连接器；归档后不再出现在公开目录")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid connectorId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireConnectorScopeAsync(
            connectorId, actor.TokenId, IsAdministrator(), writable: true, cancellationToken);
        await connectors.ArchiveAsync(connectorId, cancellationToken);
        return NoContent();
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

    private async Task RequireTargetScopeAsync(
        Guid? scopeId,
        Guid tokenId,
        CancellationToken cancellationToken)
    {
        if (scopeId is { } resolved)
        {
            await scopeAuthorization.RequireWritableAsync(
                resolved, tokenId, IsAdministrator(), cancellationToken);
            return;
        }
        RequireWildcardGrant("平台级连接器写入需要 teamlab-scope:* 资源授权");
    }

    private void RequireWildcardGrant(string message)
    {
        if (!IsAdministrator())
            throw new TeamLabApiContractException("insufficient_permission", message, 403);
    }
}
