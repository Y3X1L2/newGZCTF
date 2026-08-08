using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Traffic and Captures")]
[OpenApiTag("TeamLab - Traffic and Captures", Description = "Query traffic flows and paths, and manage bounded packet captures for a TeamLab runtime.")]
[Route("api/open/v1/teamlab/runtimes/{runtimeId:guid}")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabTrafficController(
    TeamLabTrafficApplicationService traffic,
    TeamLabCaptureArtifactStore captureArtifacts,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet("traffic/flows")]
    [OpenApiOperation("列出流量记录", "返回由 TeamLab 数据平面采集的 cursor 分页、运行时范围的流量元数据。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTrafficRead)]
    [ProducesResponseType(typeof(TeamLabTrafficFlowPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabTrafficFlowPageModel> GetFlows(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? query = null,
        [FromQuery] string? protocol = null,
        [FromQuery] string? networkKey = null,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.GetFlowsAsync(runtimeId, after, limit, query, protocol, networkKey, cancellationToken);
    }

    [HttpGet("traffic/paths")]
    [OpenApiOperation("列出关联流量路径", "返回跨参与资产与网段的端到端流量路径关联。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTrafficRead)]
    [ProducesResponseType(typeof(TeamLabTrafficPathPageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabTrafficPathPageModel> GetPaths(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? query = null,
        [FromQuery] string? protocol = null,
        [FromQuery] string? confidence = null,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        if (!TeamLabPathConfidenceFilter.TryParse(confidence, out var parsedConfidence))
            throw new TeamLabApiContractException("traffic_filter_invalid", "流量可信度筛选条件无效。", 400);
        return await traffic.GetPathsAsync(runtimeId, after, limit, query, protocol,
            parsedConfidence, cancellationToken);
    }

    [HttpGet("traffic/paths/{pathId:guid}")]
    [OpenApiOperation("获取流量路径", "返回一条关联流量路径的有序跳点与证据。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTrafficRead)]
    [ProducesResponseType(typeof(TeamLabTrafficPathModel), StatusCodes.Status200OK)]
    public async Task<TeamLabTrafficPathModel> GetPath(
        Guid runtimeId,
        Guid pathId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return await traffic.GetPathAsync(runtimeId, pathId, cancellationToken);
    }

    [HttpPost("captures")]
    [OpenApiOperation("开始抓包", "为选定的运行时分片或网段提交有上限的抓包任务。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartCapture(
        Guid runtimeId,
        CreateTeamLabCaptureModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var scopeId = await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken);
        var result = await operations.SubmitCaptureStartAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, scopeId, model, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("captures/{captureId:guid}")]
    [OpenApiOperation("获取抓包状态", "返回抓包范围、限额、进度、产物状态与保留元数据。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureRead)]
    [ProducesResponseType(typeof(OpenTeamLabCaptureModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabCaptureModel> GetCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        return (await traffic.GetCaptureAsync(runtimeId, captureId, cancellationToken)).ToOpen();
    }

    [HttpPost("captures/{captureId:guid}/stop")]
    [OpenApiOperation("停止抓包", "提交提前停止并归档正在运行的抓包任务。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StopCapture(
        Guid runtimeId,
        Guid captureId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var actor = Actor();
        var scopeId = await RequireRuntimeScopeAsync(runtimeId, true, cancellationToken);
        var result = await operations.SubmitCaptureStopAsync(
            actor.TokenId, actor.UserId, idempotencyKey, runtimeId, scopeId, captureId, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("captures/{captureId:guid}/download")]
    [OpenApiOperation("下载抓包文件", "流式返回已完成的运行时抓包归档文件。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabCaptureRead)]
    [Produces("application/x-tar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeRuntimeAsync(runtimeId, cancellationToken);
        var descriptor = await traffic.DownloadCaptureAsync(runtimeId, captureId, cancellationToken);
        Response.ContentType = "application/x-tar";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{descriptor.FileName}\"";
        try
        {
            await captureArtifacts.WriteArchiveAsync(descriptor, Response.Body, cancellationToken);
            return new EmptyResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HttpContext.Abort();
            return new EmptyResult();
        }
    }

    private async Task AuthorizeRuntimeAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        await RequireRuntimeScopeAsync(runtimeId, false, cancellationToken);
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
}
