using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/runtimes/{runtimeId:guid}/assets/{assetId:int}/control")]
public sealed class TeamLabAssetControlController(TeamLabAssetControlService controls, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<TeamLabAssetControlAvailability> Availability(Guid runtimeId, int assetId, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User) ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        return await controls.AvailabilityAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin, token);
    }

    [HttpPost]
    public async Task<ActionResult<TeamLabQueueTicketResult>> Control(Guid runtimeId, int assetId, TeamLabAssetControlCommand command, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User)
            ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        var ticket = await controls.EnqueueAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin, command, token);
        return Accepted($"/api/admin/teamlab/runtimes/{runtimeId:D}/assets/{assetId}/control/{ticket.TicketId:D}", ticket);
    }

    [HttpGet("{ticketId:guid}")]
    public async Task<TeamLabAssetControlTask> Get(Guid runtimeId, int assetId, Guid ticketId, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User) ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        Response.Headers.CacheControl = "no-store";
        return await controls.GetTaskAsync(runtimeId, assetId, ticketId, actor.Id, actor.Role >= Role.Admin, token);
    }

    [HttpPost("{ticketId:guid}/retry")]
    public async Task<ActionResult<TeamLabQueueTicketResult>> Retry(Guid runtimeId, int assetId, Guid ticketId, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User) ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        var ticket = await controls.RetryAsync(runtimeId, assetId, ticketId, actor.Id, actor.Role >= Role.Admin, token);
        return Accepted($"/api/admin/teamlab/runtimes/{runtimeId:D}/assets/{assetId}/control/{ticket.TicketId:D}", ticket);
    }
}
