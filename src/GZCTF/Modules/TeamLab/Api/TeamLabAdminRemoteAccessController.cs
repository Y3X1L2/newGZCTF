using GZCTF.Models.Data;
using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab")]
public sealed class TeamLabAdminRemoteAccessController(
    ITeamLabRemoteAccessService remoteAccess,
    UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet("runtimes/{runtimeId:guid}/assets/{assetId:int}/remote-access")]
    public async Task<TeamLabRemoteAccessAvailabilityModel> GetAvailability(
        Guid runtimeId, int assetId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await remoteAccess.GetAvailabilityAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpPost("runtimes/{runtimeId:guid}/assets/{assetId:int}/remote-sessions")]
    public async Task<ActionResult<TeamLabRemoteSessionModel>> Create(
        Guid runtimeId, int assetId, CreateTeamLabRemoteSessionModel model, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        var session = await remoteAccess.CreateAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin,
            model.Reason, cancellationToken);
        return Created($"/api/admin/teamlab/remote-sessions/{session.Id:D}", session);
    }

    [HttpGet("remote-sessions/{sessionId:guid}")]
    public async Task<TeamLabRemoteSessionModel> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await remoteAccess.GetAsync(sessionId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpGet("remote-sessions/{sessionId:guid}/connect")]
    public async Task<TeamLabRemoteConnectModel> Connect(Guid sessionId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await remoteAccess.ConnectAsync(sessionId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpDelete("remote-sessions/{sessionId:guid}")]
    public async Task<IActionResult> End(Guid sessionId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        await remoteAccess.EndAsync(sessionId, actor.Id, actor.Role >= Role.Admin, "closed_by_operator", cancellationToken);
        return NoContent();
    }

    [HttpGet("remote-sessions/{sessionId:guid}/terminal")]
    public async Task Terminal(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }
        var actor = await ActorAsync();
        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await remoteAccess.ProxyTerminalAsync(sessionId, actor.Id, actor.Role >= Role.Admin, socket, cancellationToken);
    }

    private async Task<UserInfo> ActorAsync() =>
        await users.GetUserAsync(User) ?? throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);
}
