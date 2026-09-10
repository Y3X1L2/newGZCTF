using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/remote-sessions/{sessionId:guid}/audit-files")]
public sealed class TeamLabRemoteAuditController(TeamLabRemoteAuditService audit, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<TeamLabRemoteAuditPage> List(Guid sessionId, CancellationToken token)
    {
        var actor = await ActorAsync();
        Response.Headers.CacheControl = "no-store";
        return await audit.ListAsync(sessionId, actor.Id, actor.Role >= Role.Admin, token);
    }

    [HttpPost]
    public async Task<TeamLabRemoteAuditPage> Generate(Guid sessionId, CancellationToken token)
    {
        var actor = await ActorAsync();
        await audit.GenerateAsync(sessionId, actor.Id, actor.Role >= Role.Admin, token);
        Response.Headers.CacheControl = "no-store";
        return await audit.ListAsync(sessionId, actor.Id, actor.Role >= Role.Admin, token);
    }

    [HttpGet("{fileId:long}/download")]
    public async Task<IActionResult> Download(Guid sessionId, long fileId, CancellationToken token)
    {
        var actor = await ActorAsync();
        var result = await audit.DownloadAsync(sessionId, fileId, actor.Id, actor.Role >= Role.Admin, token);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Content, "application/json", result.FileName);
    }

    private async Task<UserInfo> ActorAsync() => await users.GetUserAsync(User)
        ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
}
