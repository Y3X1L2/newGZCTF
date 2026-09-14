using System.Net.Mime;
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
[OpenApiTags("TeamLab - Remote session audit")]
[Route("api/open/v1/teamlab/remote-sessions/{sessionId:guid}/audit")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
public sealed class OpenTeamLabRemoteAuditController(TeamLabRemoteAuditService audit) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("生成远程会话操作审计", "为已结束并完成清理的会话生成或确认生命周期审计证据。")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Generate(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await audit.GenerateApiAsync(sessionId, actor.TokenId, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsRead)]
    [OpenApiOperation("查询远程会话操作审计", "返回会话生命周期证据的就绪状态、保留期限和摘要。")]
    [ProducesResponseType(typeof(OpenTeamLabRemoteAuditSummaryModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabRemoteAuditSummaryModel> Summary(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        Response.Headers.CacheControl = "no-store";
        return (await audit.ListApiAsync(sessionId, actor.TokenId, cancellationToken)).ToOpen();
    }

    [HttpGet("evidence/{evidenceId:long}/download")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsRead)]
    [OpenApiOperation("下载远程会话操作审计证据", "下载已完成完整性校验的会话生命周期 JSON 证据。")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    public async Task<IActionResult> DownloadEvidence(
        Guid sessionId,
        long evidenceId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var result = await audit.DownloadApiAsync(
            sessionId,
            evidenceId,
            actor.TokenId,
            actor.UserId,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Content, MediaTypeNames.Application.Json, result.FileName);
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份验证。", 401);
    }
}
