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
[OpenApiTags("TeamLab - Topologies")]
[OpenApiTag("TeamLab - Topologies", Description = "Design, validate, publish, and plan multi-segment TeamLab network topologies.")]
[Route("api/open/v1/teamlab")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabTopologiesController(
    ITeamLabTopologyApplicationService topologies,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet("capabilities")]
    [OpenApiOperation("获取 TeamLab 能力", "返回本平台版本支持的拓扑 schema 与功能能力。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabCapabilitiesModel), StatusCodes.Status200OK)]
    public ActionResult<TeamLabCapabilitiesModel> Capabilities() => Ok(topologies.GetCapabilities());

    [HttpPost("topologies")]
    [OpenApiOperation("创建拓扑", "提交创建可复用的 TeamLab 拓扑草稿。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        OpenCreateTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        RequireScopeGrant(model.ControlScopeId);
        var result = await operations.SubmitTopologyCreateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, model.ToInternal(), cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("topologies")]
    [OpenApiOperation("列出拓扑", "返回当前 API token 属主可见的 cursor 分页拓扑列表。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabTopologyPageModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabTopologyPageModel> List(
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        var actor = Actor();
        var scopes = await scopeAuthorization.ListReadableScopesAsync(actor.TokenId, IsAdministrator(), cancellationToken);
        return await topologies.ListPageForScopesAsync(scopes, limit, after, cancellationToken);
    }

    [HttpGet("topologies/{topologyId:guid}")]
    [OpenApiOperation("获取拓扑", "返回完整的可编辑拓扑定义。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabTopologyDetailModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabTopologyDetailModel> Get(Guid topologyId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireTopologyScopeAsync(topologyId, actor.TokenId, IsAdministrator(), false, cancellationToken);
        return (await topologies.GetAsync(topologyId, actor.UserId, true, cancellationToken)).ToOpen();
    }

    [HttpPut("topologies/{topologyId:guid}")]
    [OpenApiOperation("更新拓扑", "提交替换可编辑的拓扑定义。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Update(
        Guid topologyId,
        OpenUpdateTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await scopeAuthorization.RequireTopologyScopeAsync(
            topologyId, actor.TokenId, IsAdministrator(), true, cancellationToken);
        var result = await operations.SubmitTopologyUpdateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, topologyId, scopeId, model.ToInternal(), cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpDelete("topologies/{topologyId:guid}")]
    [OpenApiOperation("删除拓扑", "提交删除不再使用的拓扑。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(
        Guid topologyId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await scopeAuthorization.RequireTopologyScopeAsync(
            topologyId, actor.TokenId, IsAdministrator(), true, cancellationToken);
        var result = await operations.SubmitTopologyDeleteAsync(
            actor.TokenId, actor.UserId, idempotencyKey, topologyId, scopeId, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpPost("topologies/{topologyId:guid}/validate")]
    [OpenApiOperation("校验拓扑", "在不发布的情况下校验拓扑结构、寻址、连通性、资产与部署约束。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabValidationResultModel), StatusCodes.Status200OK)]
    public async Task<TeamLabValidationResultModel> Validate(Guid topologyId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireTopologyScopeAsync(topologyId, actor.TokenId, IsAdministrator(), false, cancellationToken);
        return await topologies.ValidateAsync(topologyId, actor.UserId, true, cancellationToken);
    }

    [HttpPost("topologies/{topologyId:guid}/releases")]
    [OpenApiOperation("发布拓扑版本", "校验并提交创建用于运行时部署的不可变拓扑版本。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Publish(
        Guid topologyId,
        PublishTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await scopeAuthorization.RequireTopologyScopeAsync(
            topologyId, actor.TokenId, IsAdministrator(), true, cancellationToken);
        var result = await operations.SubmitTopologyPublishAsync(
            actor.TokenId, actor.UserId, idempotencyKey, topologyId, scopeId, model, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("topologies/{topologyId:guid}/releases")]
    [OpenApiOperation("列出拓扑版本", "使用 cursor 分页返回拓扑的不可变版本列表。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabReleasePageModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabReleasePageModel> ListReleases(
        Guid topologyId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        var actor = Actor();
        await scopeAuthorization.RequireTopologyScopeAsync(topologyId, actor.TokenId, IsAdministrator(), false, cancellationToken);
        return await topologies.ListReleasesPageAsync(topologyId, actor.UserId, true, limit, after, cancellationToken);
    }

    [HttpGet("topologies/{topologyId:guid}/releases/{releaseId:guid}")]
    [OpenApiOperation("获取拓扑版本", "返回一个不可变拓扑版本及其编译后的定义。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabReleaseModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabReleaseModel> GetRelease(
        Guid topologyId,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireTopologyScopeAsync(topologyId, actor.TokenId, IsAdministrator(), false, cancellationToken);
        await scopeAuthorization.RequireReleaseScopeAsync(releaseId, actor.TokenId, IsAdministrator(), false, cancellationToken);
        return (await topologies.GetReleaseAsync(topologyId, releaseId, actor.UserId, true, cancellationToken)).ToOpen();
    }

    [HttpPost("topologies/{topologyId:guid}/releases/{releaseId:guid}/plan")]
    [OpenApiOperation("规划运行时部署", "在不创建运行时资源的情况下为版本构建部署计划。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabPlanModel), StatusCodes.Status200OK)]
    public async Task<TeamLabPlanModel> Plan(Guid topologyId, Guid releaseId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireTopologyScopeAsync(topologyId, actor.TokenId, IsAdministrator(), false, cancellationToken);
        await scopeAuthorization.RequireReleaseScopeAsync(releaseId, actor.TokenId, IsAdministrator(), false, cancellationToken);
        return await topologies.PlanAsync(topologyId, releaseId, actor.UserId, true, cancellationToken);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
    }

    private AcceptedResult AcceptedOperation(GZCTF.Modules.Audit.Application.IdempotencyBeginResult result)
    {
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    /// <summary>
    /// Open v1 endpoints are always served to API tokens, never to browser
    /// cookie sessions (the policy scheme forwards /api/open/v1 to the token
    /// scheme exclusively). Administrator capability on open v1 must therefore
    /// be expressed as a wildcard resource grant, not the token creator's role.
    /// </summary>
    private bool IsAdministrator() => User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
        ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
        type == "teamlab-scope" && id == "*");

    private void RequireScopeGrant(Guid? scopeId)
    {
        if (scopeId is null)
            throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab 控制范围。", 404);
        if (IsAdministrator()) return;
        var granted = User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
            ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
            type == "teamlab-scope" && id == scopeId.Value.ToString("D"));
        if (!granted)
            throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab 控制范围。", 404);
    }
}
