using GZCTF.Middlewares;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/runtimes/{runtimeId:guid}/differences")]
public sealed class TeamLabRuntimeDifferencesController(TeamLabRuntimeDifferenceService differences, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<RuntimeDifferencePreview> Preview(Guid runtimeId, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User) ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        Response.Headers.CacheControl = "no-store";
        return await differences.PreviewAsync(runtimeId, actor.Id, actor.Role >= Role.Admin, token);
    }

    [HttpPost("assets/{assetId:int}/repair")]
    public async Task<ActionResult<TeamLabQueueTicketResult>> Repair(Guid runtimeId, int assetId,
        TeamLabAssetControlCommand command, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User) ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        var ticket = await differences.RepairAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin, command, token);
        return Accepted($"/api/admin/teamlab/runtimes/{runtimeId:D}/assets/{assetId}/control/{ticket.TicketId:D}", ticket);
    }
}
