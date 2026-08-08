using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Rollouts")]
[Route("api/open/v1/teamlab/rollouts")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabRolloutsController(
    ITeamLabRolloutApplicationService rollouts,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [OpenApiOperation("列出 TeamLab rollouts", "列出单个已授权 control scope 内的外部 rollouts")]
    [ProducesResponseType(typeof(TeamLabRolloutPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabRolloutPageModel> List(
        [FromQuery, Required] Guid scopeId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        var actor = Actor();
        await scopeAuthorization.RequireReadableAsync(scopeId, actor.TokenId, IsAdministrator(), cancellationToken);
        return await rollouts.ListExternalAsync(scopeId, after, limit, cancellationToken);
    }

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [OpenApiOperation("创建 TeamLab rollout", "基于不可变 release 与 target 快照创建外部 rollout")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        CreateTeamLabRolloutModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireWritableAsync(
            model.ControlScopeId, actor.TokenId, IsAdministrator(), cancellationToken);
        return AcceptedOperation(await operations.SubmitRolloutCreateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, model, cancellationToken));
    }

    [HttpGet("{rolloutId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [OpenApiOperation("获取 TeamLab rollout", "返回 rollout 状态、target 数量与生命周期时间戳")]
    [ProducesResponseType(typeof(TeamLabRolloutModel), StatusCodes.Status200OK)]
    public async Task<TeamLabRolloutModel> Get(Guid rolloutId, CancellationToken cancellationToken)
    {
        var rollout = await GetAuthorizedAsync(rolloutId, writable: false, cancellationToken);
        return rollout;
    }

    [HttpGet("{rolloutId:guid}/targets")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [OpenApiOperation("列出 rollout targets", "使用稳定 cursor 返回 target 状态")]
    [ProducesResponseType(typeof(TeamLabRolloutTargetPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabRolloutTargetPageModel> Targets(
        Guid rolloutId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        await GetAuthorizedAsync(rolloutId, writable: false, cancellationToken);
        return await rollouts.ListTargetsAsync(rolloutId, after, limit, cancellationToken);
    }

    [HttpPut("{rolloutId:guid}/targets")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [OpenApiOperation("替换 rollout targets", "替换期望的 target 快照；被移除的 targets 在显式清理前保持不变")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ReplaceTargets(
        Guid rolloutId,
        ReplaceTeamLabRolloutTargetsModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var rollout = await GetAuthorizedAsync(rolloutId, writable: true, cancellationToken);
        return AcceptedOperation(await operations.SubmitRolloutReplaceTargetsAsync(
            Actor().TokenId, Actor().UserId, idempotencyKey, rolloutId, model,
            rollout.ControlScopeId ?? throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab control scope", 404),
            cancellationToken));
    }

    [HttpPost("{rolloutId:guid}/prepare")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [OpenApiOperation("准备 TeamLab rollout", "启动 image 准备与 target 供给协调")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Prepare(Guid rolloutId, [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken) =>
        SubmitLifecycle(rolloutId, idempotencyKey, (actor, scopeId) => operations.SubmitRolloutPrepareAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, scopeId, cancellationToken));

    [HttpPost("{rolloutId:guid}/open-access")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("打开 rollout 访问", "仅在所有期望 targets 就绪后开放玩家访问")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> OpenAccess(Guid rolloutId, [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken) =>
        SubmitLifecycle(rolloutId, idempotencyKey, (actor, scopeId) => operations.SubmitRolloutSetAccessAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, scopeId, true, cancellationToken));

    [HttpPost("{rolloutId:guid}/close-access")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("关闭 rollout 访问", "关闭玩家访问而不销毁 rollout")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> CloseAccess(Guid rolloutId, [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken) =>
        SubmitLifecycle(rolloutId, idempotencyKey, (actor, scopeId) => operations.SubmitRolloutSetAccessAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, scopeId, false, cancellationToken));

    [HttpPost("{rolloutId:guid}/drain")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("清理 TeamLab rollout", "关闭访问并按有界批次销毁所有 target runtimes")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Drain(Guid rolloutId, [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken) =>
        SubmitLifecycle(rolloutId, idempotencyKey, (actor, scopeId) => operations.SubmitRolloutDrainAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, scopeId, cancellationToken));

    [HttpPost("{rolloutId:guid}/targets/{targetId:guid}/rebuild")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("重建失败的 rollout target", "请求显式重建单个失败的 target")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Rebuild(
        Guid rolloutId,
        Guid targetId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var rollout = await GetAuthorizedAsync(rolloutId, writable: true, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitRolloutRebuildTargetAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, targetId,
            rollout.ControlScopeId ?? throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab control scope", 404),
            cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpPost("{rolloutId:guid}/targets/{targetId:guid}/pause")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("暂停单个 rollout target", "在原节点上挂起该 target 的运行时，保留运行时身份、网络与容量预留")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> PauseTarget(
        Guid rolloutId,
        Guid targetId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) =>
        SubmitTargetLifecycle(rolloutId, targetId, idempotencyKey, pause: true, cancellationToken);

    [HttpPost("{rolloutId:guid}/targets/{targetId:guid}/resume")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("恢复单个 rollout target", "仅在原始分配节点上恢复该 target 的运行时")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> ResumeTarget(
        Guid rolloutId,
        Guid targetId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) =>
        SubmitTargetLifecycle(rolloutId, targetId, idempotencyKey, pause: false, cancellationToken);

    [HttpPost("{rolloutId:guid}/targets/{targetId:guid}/restart")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("重启单个 rollout target", "按原发布版本受控清理并重新部署该 target 的运行时")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> RestartTarget(
        Guid rolloutId,
        Guid targetId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) =>
        SubmitTargetRestart(rolloutId, targetId, idempotencyKey, cancellationToken);

    [HttpPost("{rolloutId:guid}/pause")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [OpenApiOperation("暂停 TeamLab rollout", "暂停 rollout 协调，已提交的目标与运行时保持不变")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Pause(Guid rolloutId, [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken) =>
        SubmitLifecycle(rolloutId, idempotencyKey, (actor, scopeId) => operations.SubmitRolloutPauseAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, scopeId, cancellationToken));

    [HttpPost("{rolloutId:guid}/resume")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [OpenApiOperation("恢复 TeamLab rollout", "从暂停状态恢复 rollout 协调")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Resume(Guid rolloutId, [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken) =>
        SubmitLifecycle(rolloutId, idempotencyKey, (actor, scopeId) => operations.SubmitRolloutResumeAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, scopeId, cancellationToken));

    [HttpPost("{rolloutId:guid}/archive")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [OpenApiOperation("归档 TeamLab rollout", "归档已完全清理的 rollout 并保留只读历史")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Archive(Guid rolloutId, [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken) =>
        SubmitLifecycle(rolloutId, idempotencyKey, (actor, scopeId) => operations.SubmitRolloutArchiveAsync(
            actor.TokenId, actor.UserId, idempotencyKey, rolloutId, scopeId, cancellationToken));

    private async Task<IActionResult> SubmitTargetLifecycle(
        Guid rolloutId,
        Guid targetId,
        string idempotencyKey,
        bool pause,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var (scopeId, runtimeId) = await ResolveTargetAsync(rolloutId, targetId, cancellationToken);
        var result = await operations.SubmitRolloutTargetLifecycleAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, rolloutId, targetId, scopeId, pause,
            cancellationToken);
        return AcceptedOperation(result);
    }

    private async Task<IActionResult> SubmitTargetRestart(
        Guid rolloutId,
        Guid targetId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var (scopeId, runtimeId) = await ResolveTargetAsync(rolloutId, targetId, cancellationToken);
        var result = await operations.SubmitRolloutTargetRestartAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, rolloutId, targetId, scopeId,
            cancellationToken);
        return AcceptedOperation(result);
    }

    private async Task<(Guid ScopeId, Guid RuntimeId)> ResolveTargetAsync(
        Guid rolloutId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var rollout = await GetAuthorizedAsync(rolloutId, writable: true, cancellationToken);
        var target = await rollouts.GetTargetAsync(rolloutId, targetId, cancellationToken)
            ?? throw new TeamLabApiContractException("rollout_target_not_found", "未找到 rollout target", 404);
        return (rollout.ControlScopeId ?? throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab control scope", 404),
            target.RuntimeId ?? throw new TeamLabApiContractException("rollout_target_not_ready", "target 尚无运行中的运行时", 409));
    }

    private async Task<IActionResult> SubmitLifecycle(
        Guid rolloutId,
        string idempotencyKey,
        Func<(Guid TokenId, Guid UserId), Guid, Task<IdempotencyBeginResult>> submit)
    {
        var rollout = await GetAuthorizedAsync(rolloutId, writable: true, HttpContext.RequestAborted);
        var actor = Actor();
        return AcceptedOperation(await submit(actor,
            rollout.ControlScopeId ?? throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab control scope", 404)));
    }

    private async Task<TeamLabRolloutModel> GetAuthorizedAsync(
        Guid rolloutId,
        bool writable,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireRolloutScopeAsync(
            rolloutId, actor.TokenId, IsAdministrator(), writable, cancellationToken);
        var rollout = await rollouts.GetAsync(rolloutId, cancellationToken)
            ?? throw new TeamLabApiContractException("rollout_not_found", "未找到 TeamLab rollout", 404);
        return rollout;
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

    private AcceptedResult AcceptedOperation(IdempotencyBeginResult result)
    {
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }
}
