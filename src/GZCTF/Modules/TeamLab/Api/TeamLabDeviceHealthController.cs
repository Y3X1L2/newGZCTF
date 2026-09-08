using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/runtimes/{runtimeId:guid}/device-health")]
public sealed class TeamLabDeviceHealthController(TeamLabDeviceObservationService observations, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<TeamLabDeviceHealthModel>> Read(Guid runtimeId, CancellationToken token)
    {
        var user = await users.GetUserAsync(User) ?? throw new TeamLabApiContractException("authentication_required", "需要登录。", 401);
        Response.Headers.CacheControl = "no-store";
        return await observations.ReadAsync(runtimeId, user.Id, user.Role >= Role.Admin, token);
    }
}
