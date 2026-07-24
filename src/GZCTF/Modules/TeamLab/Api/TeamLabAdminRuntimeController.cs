using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/runtimes/{runtimeId:guid}")]
public sealed class TeamLabAdminRuntimeController(
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabRuntimeProjectionService projections,
    TeamLabTrafficApplicationService traffic,
    TeamLabCaptureArtifactStore captureArtifacts,
    TeamLabAuthorizationService authorization,
    UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<TeamLabRuntimeProjectionModel> Get(Guid runtimeId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(runtimeId, cancellationToken);
        return await runtimes.GetAsync(runtimeId, cancellationToken);
    }

    [HttpGet("events")]
    public async Task<IReadOnlyList<TeamLabRuntimeEventModel>> Events(
        Guid runtimeId,
        [FromQuery] long after = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(runtimeId, cancellationToken);
        return await projections.GetEventsAsync(runtimeId, after, Math.Clamp(limit, 1, 200), cancellationToken);
    }

    [HttpGet("traffic/flows")]
    public async Task<TeamLabTrafficFlowPageModel> Flows(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(runtimeId, cancellationToken);
        return await traffic.GetFlowsAsync(runtimeId, after, Math.Clamp(limit, 1, 200), cancellationToken);
    }

    [HttpPost("captures")]
    public async Task<ActionResult<TeamLabCaptureModel>> StartCapture(
        Guid runtimeId,
        CreateTeamLabCaptureModel model,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(runtimeId, cancellationToken);
        var capture = await traffic.StartCaptureAsync(runtimeId, model, cancellationToken);
        return Created($"/api/admin/teamlab/runtimes/{runtimeId:D}/captures/{capture.Id:D}", capture);
    }

    [HttpGet("captures/{captureId:guid}")]
    public async Task<TeamLabCaptureModel> GetCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(runtimeId, cancellationToken);
        return await traffic.GetCaptureAsync(runtimeId, captureId, cancellationToken);
    }

    [HttpPost("captures/{captureId:guid}/stop")]
    public async Task<TeamLabCaptureModel> StopCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(runtimeId, cancellationToken);
        return await traffic.StopCaptureAsync(runtimeId, captureId, cancellationToken);
    }

    [HttpGet("captures/{captureId:guid}/download")]
    public async Task<IActionResult> DownloadCapture(Guid runtimeId, Guid captureId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(runtimeId, cancellationToken);
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

    private async Task AuthorizeAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(User)
            ?? throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);
        await authorization.RequireRuntimeManagerAsync(
            runtimeId,
            actor.Id,
            actor.Role == Role.Admin,
            cancellationToken);
    }
}
