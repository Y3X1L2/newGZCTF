using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

/// <summary>
/// Token-authenticated remote sessions, one-time VM connection links and PTY WebSocket transport.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Remote sessions")]
[Route("api/open/v1/teamlab")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabRemoteSessionsController(
    ITeamLabRemoteAccessService remoteAccess,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet("runtimes/{runtimeId:guid}/remote-access")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsRead)]
    [OpenApiOperation("查询远程访问可用性", "返回运行时全部资产的可用协议与不可用原因。")]
    [ProducesResponseType(typeof(IReadOnlyList<OpenTeamLabRemoteAvailabilityModel>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<OpenTeamLabRemoteAvailabilityModel>> Availability(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        await RequireRuntimeScopeAsync(runtimeId, writable: false, cancellationToken);
        var actor = Actor();
        return (await remoteAccess.GetAvailabilityBatchAsync(runtimeId, actor.UserId, administrator: false, cancellationToken))
            .Select(item => item.ToOpen()).ToArray();
    }

    [HttpPost("runtimes/{runtimeId:guid}/assets/{assetId:int}/remote-sessions")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("创建远程会话", "为单个资产创建限时会话；VM 使用 connect，容器使用携带 Bearer 身份的 terminal WebSocket。")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        Guid runtimeId,
        int assetId,
        OpenCreateTeamLabRemoteSessionModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var scopeId = await RequireRuntimeScopeAsync(runtimeId, writable: true, cancellationToken);
        var actor = Actor();
        var result = await operations.SubmitRemoteSessionCreateAsync(actor.TokenId, actor.UserId,
            idempotencyKey, runtimeId, scopeId, assetId, model.Reason, cancellationToken, model.VncConsole);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("remote-sessions/{sessionId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsRead)]
    [OpenApiOperation("查询远程会话", "返回会话状态、协议、访问原因与时间线。")]
    [ProducesResponseType(typeof(OpenTeamLabRemoteSessionModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabRemoteSessionModel> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        var session = await remoteAccess.GetAsync(sessionId, actor.UserId, administrator: false, cancellationToken);
        await RequireRuntimeScopeAsync(session.RuntimeId, writable: false, cancellationToken);
        return session.ToOpen();
    }

    [HttpDelete("remote-sessions/{sessionId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("关闭远程会话", "主动结束会话并回收转发通道；重复关闭幂等。")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> End(Guid sessionId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken cancellationToken)
    {
        var actor = Actor();
        var session = await remoteAccess.GetAsync(sessionId, actor.UserId, false, cancellationToken);
        var scopeId = await RequireRuntimeScopeAsync(session.RuntimeId, true, cancellationToken);
        var result = await operations.SubmitRemoteSessionEndAsync(actor.TokenId, actor.UserId,
            idempotencyKey, session.RuntimeId, scopeId, sessionId, cancellationToken);
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("remote-sessions/{sessionId:guid}/connect")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("获取一次性远程连接", "消费 VM 会话连接入口；入口只返回一次，过期需重新创建会话。")]
    public async Task<OpenTeamLabRemoteConnectModel> Connect(Guid sessionId, CancellationToken cancellationToken)
    {
        await RequireSessionScopeAsync(sessionId, cancellationToken);
        var actor = Actor();
        Response.Headers.CacheControl = "no-store";
        var result = await remoteAccess.ConnectAsync(sessionId, actor.UserId, false, cancellationToken);
        return new(result.Url, result.ExpiresAt);
    }

    [HttpGet("remote-sessions/{sessionId:guid}/terminal")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("连接容器终端", "WebSocket：二进制消息为 PTY 字节；文本 JSON 支持 resize、input、signal。Bearer token 必须在 Authorization 请求头中。")]
    public async Task Terminal(Guid sessionId, CancellationToken cancellationToken)
    {
        await RequireSessionScopeAsync(sessionId, cancellationToken);
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }
        var actor = Actor();
        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await remoteAccess.ProxyTerminalAsync(sessionId, actor.UserId, false, socket, cancellationToken);
    }

    private async Task RequireSessionScopeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var actor = Actor();
        var session = await remoteAccess.GetAsync(sessionId, actor.UserId, false, cancellationToken);
        await RequireRuntimeScopeAsync(session.RuntimeId, writable: true, cancellationToken);
    }

    private async Task<Guid> RequireRuntimeScopeAsync(Guid runtimeId, bool writable, CancellationToken cancellationToken)
    {
        var actor = Actor();
        return await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, actor.TokenId, IsAdministrator(), writable, cancellationToken);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份验证。", 401);
    }

    private bool IsAdministrator() => User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
        ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
        type == "teamlab-scope" && id == "*");
}
