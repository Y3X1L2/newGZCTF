using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Runtimes")]
[OpenApiTag("TeamLab - Runtimes", Description = "Create, inspect, reset, destroy, and access deployed TeamLab environments.")]
[Route("api/open/v1/teamlab/runtimes")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabRuntimesController(
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabRuntimeProjectionService projections,
    TeamLabRuntimeOperationApplicationService operations,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeLifecycleGuard lifecycleGuard,
    TeamLabAccessGrantService access,
    AppDbContext context,
    TeamLabEventRecorder eventRecorder) : ControllerBase
{
    [HttpPost]
    [OpenApiOperation("创建运行时", "为单个队伍或自动化属主提交已发布拓扑版本的部署任务。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        CreateTeamLabRuntimeModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var scopeId = await scopeAuthorization.RequireReleaseScopeAsync(
            model.ReleaseId, actor.TokenId, IsAdministrator(), true, cancellationToken);
        var result = await operations.SubmitCreateAsync(actor.TokenId, actor.UserId, idempotencyKey,
            "POST:/api/open/v1/teamlab/runtimes", scopeId, model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("{runtimeId:guid}")]
    [OpenApiOperation("获取运行时", "返回聚合的运行时、分片、网络与资产状态。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [ProducesResponseType(typeof(OpenTeamLabRuntimeModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabRuntimeModel> Get(Guid runtimeId, CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return (await runtimes.GetAsync(runtimeId, cancellationToken)).ToOpen();
    }

    [HttpPost("{runtimeId:guid}/reset")]
    [OpenApiOperation("重置运行时", "按运行时的发布版本与可选覆盖配置提交受控清理并重新部署。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Reset(
        Guid runtimeId,
        ResetTeamLabRuntimeModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        await RequireDirectLifecycleControlAsync(runtimeId, cancellationToken);
        var result = await operations.SubmitResetAsync(actor.TokenId, actor.UserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/reset", runtimeId,
            await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken), model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpDelete("{runtimeId:guid}")]
    [OpenApiOperation("销毁运行时", "提交清理运行时的所有分片、资产、路由、抓包与访问授权。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Destroy(
        Guid runtimeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        await RequireDirectLifecycleControlAsync(runtimeId, cancellationToken);
        var result = await operations.SubmitDestroyAsync(actor.TokenId, actor.UserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/runtimes/{runtimeId:D}", runtimeId,
            await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken), cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("{runtimeId:guid}/pause")]
    [OpenApiOperation("暂停运行时", "在原节点上挂起工作负载，同时保留运行时身份、网络、磁盘、地址、访问状态与容量预留。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Pause(
        Guid runtimeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        await RequireDirectLifecycleControlAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitPauseAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId,
            await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken), cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("{runtimeId:guid}/resume")]
    [OpenApiOperation("恢复运行时", "仅在原始分配节点上恢复，不会重新调度或下载镜像；原节点不可用时返回 resume_blocked。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Resume(
        Guid runtimeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        await RequireDirectLifecycleControlAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitResumeAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId,
            await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken), cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("{runtimeId:guid}/protocol-events")]
    [OpenApiOperation("上报协议事件", "设备/传感器把去敏的协议事件（如点位读写、握手、告警）写入运行时事件流，可用 events?stage=protocol 查询。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportProtocolEvent(
        Guid runtimeId,
        TeamLabProtocolEventReportModel model,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, actor.TokenId, IsAdministrator(), writable: true, cancellationToken);
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (runtime.Status != TeamLabRuntimeStatus.Running)
            throw new TeamLabApiContractException("runtime_not_running", "仅运行中运行时接受协议事件上报", 409);

        var detail = new Dictionary<string, object?>
        {
            ["type"] = model.Type,
            ["source"] = model.Source,
            ["occurredAt"] = model.OccurredAt?.ToString("O"),
            ["parameters"] = model.Parameters,
        };
        eventRecorder.Record(
            runtime,
            "protocol",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.ProtocolEvent,
            OperationalEventOutcome.Succeeded,
            "收到设备协议事件",
            detail: detail);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(new { runtimeId, stage = "protocol", type = model.Type, source = model.Source });
    }

    [HttpGet("{runtimeId:guid}/events")]
    [OpenApiOperation("列出运行时事件", "返回 cursor 分页的生命周期与部署事件，用于排障与审计。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [ProducesResponseType(typeof(OpenTeamLabRuntimeEventPageModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabRuntimeEventPageModel> Events(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] int? generation = null,
        [FromQuery] string? stage = null,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await projections.GetEventsAsync(runtimeId, after, limit, generation, stage, cancellationToken);
    }

    [HttpPost("{runtimeId:guid}/access-grants")]
    [OpenApiOperation("创建 WireGuard 访问授权", "提交创建短时效、单次下载的玩家访问配置。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateAccessGrant(
        Guid runtimeId,
        TeamLabAccessGrantCreateModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(model.Type, "WireGuard", StringComparison.OrdinalIgnoreCase))
            throw new TeamLabApiContractException("topology_invalid", "仅支持 WireGuard 访问授权。", 422);
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitAccessGrantCreateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId,
            await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken), model, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("{runtimeId:guid}/access-grants/{grantId:guid}/download")]
    [OpenApiOperation("下载访问配置", "消耗一次性下载 token 并返回 WireGuard 配置文件。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [Produces("application/x-wireguard-profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadAccessConfiguration(
        Guid runtimeId,
        Guid grantId,
        [FromQuery, Required] string token,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var result = await access.ConsumeConfigurationAsync(runtimeId, grantId, token, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(result.Configuration), "application/x-wireguard-profile", result.FileName);
    }

    [HttpDelete("{runtimeId:guid}/access-grants/{grantId:guid}")]
    [OpenApiOperation("撤销访问授权", "提交撤销并清理现有的运行时访问授权。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RevokeAccessGrant(
        Guid runtimeId,
        Guid grantId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitAccessGrantRevokeAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId,
            await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken), grantId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task AuthorizeRuntimeAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, actor.TokenId, IsAdministrator(), false, cancellationToken);
    }

    private async Task<Guid> RequireRuntimeScopeAsync(
        Guid runtimeId,
        bool writable,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        return await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, actor.TokenId, IsAdministrator(), writable, cancellationToken);
    }

    private bool IsAdministrator() => User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
        ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
        type == "teamlab-scope" && id == "*");

    private async Task RequireDirectLifecycleControlAsync(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        if (await lifecycleGuard.IsRolloutManagedAsync(runtimeId, cancellationToken))
            throw new TeamLabApiContractException(
                "runtime_managed_by_rollout",
                "此运行时由比赛 rollout 管理，请使用比赛生命周期 API。",
                409);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
    }
}
