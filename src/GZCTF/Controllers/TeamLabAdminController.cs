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
    TeamLabTrafficCaptureService captureService,
    TeamLabTrafficFlowService flowService,
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

    [HttpGet("teams/{teamId:int}/captures")]
    [RequireAdmin]
    public async Task<IActionResult> Captures(int gameId, int teamId, CancellationToken token) =>
        Ok(await captureService.ListJobsAsync(gameId, teamId, token));

    [HttpPost("teams/{teamId:int}/captures/start")]
    [RequireAdmin]
    public async Task<IActionResult> StartCapture(int gameId, int teamId,
        [FromBody] TeamLabCaptureStartModel model, CancellationToken token) =>
        ToActionResult(await captureService.StartCaptureAsync(gameId, teamId, model, token));

    [HttpPost("teams/{teamId:int}/captures/{jobId:int}/stop")]
    [RequireAdmin]
    public async Task<IActionResult> StopCapture(int gameId, int teamId, int jobId, CancellationToken token) =>
        ToActionResult(await captureService.StopCaptureAsync(gameId, teamId, jobId, token));

    [HttpPost("teams/{teamId:int}/captures/{jobId:int}/status")]
    [RequireAdmin]
    public async Task<IActionResult> RefreshCaptureStatus(int gameId, int teamId, int jobId,
        CancellationToken token) =>
        ToActionResult(await captureService.RefreshCaptureStatusAsync(gameId, teamId, jobId, token));

    [HttpGet("teams/{teamId:int}/captures/{jobId:int}/download")]
    [RequireAdmin]
    public async Task<IActionResult> DownloadCapture(int gameId, int teamId, int jobId, CancellationToken token)
    {
        var result = await captureService.DownloadCaptureAsync(gameId, teamId, jobId, token);
        if (!result.Success || result.Stream is null)
        {
            result.Owner?.Dispose();
            return new BadRequestObjectResult(new { message = result.Message });
        }

        if (result.Owner is not null)
            Response.RegisterForDispose(result.Owner);
        return File(result.Stream, result.ContentType, result.FileName, enableRangeProcessing: true);
    }

    [HttpPost("teams/{teamId:int}/flows/refresh")]
    [RequireAdmin]
    public async Task<IActionResult> RefreshFlows(int gameId, int teamId, CancellationToken token) =>
        ToActionResult(await flowService.RefreshRuntimeAsync(gameId, teamId, token));

    [HttpGet("teams/{teamId:int}/flows")]
    [RequireAdmin]
    public async Task<IActionResult> Flows(int gameId, int teamId, [FromQuery] int count, CancellationToken token) =>
        Ok(await flowService.GetRecentFlowsAsync(gameId, teamId, count <= 0 ? 100 : count, token));

    public static IActionResult ToActionResult(TeamLabPlanResult result) =>
        result.Success ? new OkObjectResult(result) : new BadRequestObjectResult(result);

    public static IActionResult ToActionResult(TeamLabDeploymentResult result) =>
        result.Success
            ? new OkObjectResult(result)
            : result.Queue is not null
                ? new AcceptedResult((string?)null, result)
                : new BadRequestObjectResult(result);

    public static IActionResult ToActionResult(TeamLabTrafficCaptureResult result) =>
        result.Success ? new OkObjectResult(result) : new BadRequestObjectResult(result);

    public static IActionResult ToActionResult(TeamLabTrafficFlowRefreshResult result) =>
        result.Success ? new OkObjectResult(result) : new BadRequestObjectResult(result);

    public static CancellationTokenSource CreateDeployOperationToken(CancellationToken applicationStopping)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
    }
}
