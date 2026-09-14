using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/teamlab/runtimes/{runtimeId:guid}")]
public sealed class TeamLabServiceAccessController(TeamLabServiceAccessService service) : ControllerBase
{
    [HttpPost("assets/{assetId:int}/service-access")]
    public async Task<ActionResult<TeamLabServiceAccessModel>> Create(
        Guid runtimeId, int assetId, CreateTeamLabServiceAccessModel model, CancellationToken token)
    {
        var result = await service.CreateAsync(runtimeId, assetId, model, token);
        return Created($"/api/admin/teamlab/runtimes/{runtimeId:D}/service-access/{result.Id:D}", result);
    }

    [HttpGet("service-access")]
    public Task<IReadOnlyList<TeamLabServiceAccessModel>> List(Guid runtimeId, CancellationToken token) =>
        service.ListAsync(runtimeId, token);

    [HttpDelete("service-access/{accessId:guid}")]
    public Task<TeamLabServiceAccessModel> Remove(Guid runtimeId, Guid accessId, CancellationToken token) =>
        service.RemoveAsync(runtimeId, accessId, token);
}
