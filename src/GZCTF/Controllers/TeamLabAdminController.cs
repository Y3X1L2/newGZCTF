using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Services.TeamLab;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/admin/teamlab/games/{gameId:int}")]
[Produces(MediaTypeNames.Application.Json)]
public class TeamLabAdminController(
    TeamLabPlanService planService,
    TeamLabDeploymentService deploymentService,
    IHostApplicationLifetime lifetime) : ControllerBase
{
    [HttpPost("teams/{teamId:int}/plan")]
    [RequireAdmin]
    public async Task<IActionResult> Plan(int gameId, int teamId, CancellationToken token) =>
        ToActionResult(await planService.PlanRuntimeAsync(gameId, teamId, token));

    [HttpPost("teams/{teamId:int}/deploy")]
    [RequireAdmin]
    public async Task<IActionResult> Deploy(int gameId, int teamId)
    {
        using var operationToken = CreateDeployOperationToken(lifetime.ApplicationStopping);
        return ToActionResult(await deploymentService.DeployRuntimeAsync(gameId, teamId, operationToken.Token));
    }

    [HttpPost("teams/{teamId:int}/destroy")]
    [RequireAdmin]
    public async Task<IActionResult> Destroy(int gameId, int teamId, CancellationToken token) =>
        ToActionResult(await deploymentService.DestroyRuntimeAsync(gameId, teamId, token));

    [HttpGet("teams/{teamId:int}/events")]
    [RequireAdmin]
    public async Task<IActionResult> Events(int gameId, int teamId, CancellationToken token) =>
        Ok(await deploymentService.GetEventsAsync(gameId, teamId, token));

    public static IActionResult ToActionResult(TeamLabPlanResult result) =>
        result.Success ? new OkObjectResult(result) : new BadRequestObjectResult(result);

    public static IActionResult ToActionResult(TeamLabDeploymentResult result) =>
        result.Success
            ? new OkObjectResult(result)
            : result.Queue is not null
                ? new AcceptedResult((string?)null, result)
                : new BadRequestObjectResult(result);

    public static CancellationTokenSource CreateDeployOperationToken(CancellationToken applicationStopping)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
    }
}
