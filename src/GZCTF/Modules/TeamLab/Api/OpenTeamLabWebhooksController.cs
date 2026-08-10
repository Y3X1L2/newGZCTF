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
[OpenApiTags("TeamLab - Webhooks")]
[OpenApiTag("TeamLab - Webhooks", Description = "Subscribe to signed at-least-once TeamLab event notifications.")]
[Route("api/open/v1/teamlab/webhooks")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabWebhooksController(
    TeamLabWebhookService webhooks,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpPost]
    [OpenApiOperation("创建 webhook 订阅", "在指定控制范围内创建 https 端点的事件通知订阅；端点必须可公网解析。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        CreateTeamLabWebhookModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await RequireScopeAsync(model.ControlScopeId, true, cancellationToken);
        var result = await operations.SubmitWebhookCreateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet]
    [OpenApiOperation("列出 webhook 订阅", "按控制范围返回 cursor 分页的订阅列表。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabWebhookPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabWebhookPageModel> List(
        [FromQuery, Required] Guid scopeId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        var actor = Actor();
        await RequireScopeAsync(scopeId, false, cancellationToken);
        return await webhooks.ListAsync(scopeId, after, limit, cancellationToken);
    }

    [HttpGet("{webhookId:guid}")]
    [OpenApiOperation("获取 webhook 订阅", "返回订阅详情与最近投递失败记录；签名密钥永不返回。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabWebhookModel), StatusCodes.Status200OK)]
    public async Task<TeamLabWebhookModel> Get(Guid webhookId, CancellationToken cancellationToken)
    {
        await RequireWebhookScopeAsync(webhookId, false, cancellationToken);
        return await webhooks.GetAsync(webhookId, cancellationToken);
    }

    [HttpDelete("{webhookId:guid}")]
    [OpenApiOperation("撤销 webhook 订阅", "停止后续投递；已排队投递不会回滚。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Revoke(
        Guid webhookId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await RequireWebhookScopeAsync(webhookId, true, cancellationToken);
        var result = await operations.SubmitWebhookRevokeAsync(
            actor.TokenId, actor.UserId, idempotencyKey, webhookId, scopeId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("{webhookId:guid}/replay")]
    [OpenApiOperation("重放 webhook 事件", "从指定事件 ID 重新投递不可变信封；不推进投递游标，也不会创建新的运行时操作。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Replay(
        Guid webhookId,
        [FromQuery, Range(1, long.MaxValue)] long? fromEventId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await RequireWebhookScopeAsync(webhookId, true, cancellationToken);
        var result = await operations.SubmitWebhookReplayAsync(
            actor.TokenId, actor.UserId, idempotencyKey, webhookId, scopeId, fromEventId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task RequireScopeAsync(Guid scopeId, bool writable, CancellationToken cancellationToken)
    {
        var actor = Actor();
        if (writable)
            await scopeAuthorization.RequireWritableAsync(scopeId, actor.TokenId, IsAdministrator(), cancellationToken);
        else
            await scopeAuthorization.RequireReadableAsync(scopeId, actor.TokenId, IsAdministrator(), cancellationToken);
    }

    private async Task<Guid> RequireWebhookScopeAsync(
        Guid webhookId,
        bool writable,
        CancellationToken cancellationToken)
    {
        var model = await webhooks.GetAsync(webhookId, cancellationToken);
        await RequireScopeAsync(model.ControlScopeId, writable, cancellationToken);
        return model.ControlScopeId;
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
    }

    private bool IsAdministrator() => User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
        ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
        type == "teamlab-scope" && id == "*");
}
