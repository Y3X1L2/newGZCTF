using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Admin;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/runtimes")]
public sealed class TeamLabAdminRuntimeController(
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabRuntimeProjectionService projections,
    TeamLabAdminQueryService queries,
    TeamLabReleaseImagePreparationService imagePreparation,
    TeamLabTrafficApplicationService traffic,
    TeamLabCaptureArtifactStore captureArtifacts,
    TeamLabAuthorizationService authorization,
    TeamLabAccessGrantService access,
    ILogRepository logs,
    UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<TeamLabAdminRuntimePageModel> List(
        [FromQuery] Guid? topologyId = null,
        [FromQuery] string? after = null,
        [FromQuery] int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync();
        return await queries.ListTrialRuntimesAsync(
            topologyId, actor.Id, actor.Role >= Role.Admin, after, limit, cancellationToken);
    }

    [HttpPost("trials")]
    public async Task<ActionResult<TeamLabRuntimeProjectionModel>> CreateTrial(
        CreateTeamLabTrialRuntimeModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        var key = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var ownerId = await queries.RequireReleaseOwnerAsync(
            model.ReleaseId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
        await imagePreparation.QueueAsync(model.ReleaseId, cancellationToken);
        var command = new CreateTeamLabRuntimeModel(
            model.ReleaseId,
            model.ExternalReference,
            model.Constraints,
            model.Overlays);
        var requestHash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));
        var created = await runtimes.PlanAndEnqueueAsync(
            command,
            actor.Id,
            ownerId,
            requestHash,
            key,
            null,
            "TeamLab 试运行",
            cancellationToken);
        var projection = await runtimes.GetAsync(created.RuntimePublicId, cancellationToken);
        return Accepted($"/api/admin/teamlab/runtimes/{created.RuntimePublicId:D}", projection);
    }

    [HttpGet("{runtimeId:guid}")]
    public async Task<TeamLabRuntimeProjectionModel> Get(Guid runtimeId, CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.StateRead, cancellationToken);
        return await runtimes.GetAsync(runtimeId, cancellationToken);
    }

    [HttpGet("{runtimeId:guid}/logs")]
    public async Task<LogMessagePageModel> Logs(
        Guid runtimeId,
        [FromQuery] LogQueryModel query,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.MetadataRead, cancellationToken);
        query.CorrelationId = null;
        query.ResourceType = "teamlab-runtime";
        query.ResourceId = runtimeId.ToString("D");
        try
        {
            return await logs.GetLogs(query, cancellationToken);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException("invalid_cursor", "日志 cursor 无效。", 400);
        }
    }

    [HttpPost("{runtimeId:guid}/reset")]
    public async Task<ActionResult<TeamLabRuntimeProjectionModel>> Reset(
        Guid runtimeId,
        ResetTeamLabRuntimeModel model,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.LifecycleManage, cancellationToken);
        await runtimes.ResetAndEnqueueAsync(runtimeId, model, null, cancellationToken);
        return Accepted($"/api/admin/teamlab/runtimes/{runtimeId:D}",
            await runtimes.GetAsync(runtimeId, cancellationToken));
    }

    [HttpDelete("{runtimeId:guid}")]
    public async Task<ActionResult<TeamLabRuntimeProjectionModel>> Destroy(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        await authorization.RequirePermissionAsync(
            runtimeId, actor.Id, actor.Role >= Role.Admin,
            TeamLabRuntimePermission.LifecycleManage, cancellationToken);
        await runtimes.DestroyAndEnqueueAsync(runtimeId, null, actor.Id, cancellationToken);
        return Accepted($"/api/admin/teamlab/runtimes/{runtimeId:D}",
            await runtimes.GetAsync(runtimeId, cancellationToken));
    }

    [HttpGet("{runtimeId:guid}/events")]
    public async Task<IReadOnlyList<TeamLabRuntimeEventModel>> Events(
        Guid runtimeId,
        [FromQuery] long after = 0,
        [FromQuery] int limit = 100,
        [FromQuery] int? generation = null,
        [FromQuery] string? stage = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.MetadataRead, cancellationToken);
        return await projections.GetEventsAsync(runtimeId, after, Math.Clamp(limit, 1, 200), generation, stage,
            cancellationToken);
    }

    [HttpGet("{runtimeId:guid}/traffic/flows")]
    public async Task<TeamLabTrafficFlowPageModel> Flows(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery] int limit = 100,
        [FromQuery] string? query = null,
        [FromQuery] string? protocol = null,
        [FromQuery] string? networkKey = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.MetadataRead, cancellationToken);
        return await traffic.GetFlowsAsync(runtimeId, after, Math.Clamp(limit, 1, 200), query, protocol, networkKey, cancellationToken);
    }

    [HttpGet("{runtimeId:guid}/traffic/paths")]
    public async Task<TeamLabTrafficPathPageModel> Paths(
        Guid runtimeId,
        [FromQuery] string? after = null,
        [FromQuery] int limit = 100,
        [FromQuery] string? query = null,
        [FromQuery] string? protocol = null,
        [FromQuery] string? confidence = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.MetadataRead, cancellationToken);
        if (!TeamLabPathConfidenceFilter.TryParse(confidence, out var parsedConfidence))
            throw new TeamLabApiContractException("traffic_filter_invalid", "流量可信度筛选条件无效。", 400);
        return await traffic.GetPathsAsync(runtimeId, after, Math.Clamp(limit, 1, 200), query, protocol,
            parsedConfidence, cancellationToken);
    }

    [HttpGet("{runtimeId:guid}/traffic/paths/{pathId:guid}")]
    public async Task<TeamLabTrafficPathModel> Path(
        Guid runtimeId,
        Guid pathId,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.MetadataRead, cancellationToken);
        return await traffic.GetPathAsync(runtimeId, pathId, cancellationToken);
    }

    [HttpPost("{runtimeId:guid}/access-grants")]
    public async Task<ActionResult<TeamLabAccessGrantModel>> CreateAccessGrant(
        Guid runtimeId,
        TeamLabAccessGrantCreateModel model,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(model.Type, "WireGuard", StringComparison.OrdinalIgnoreCase))
            throw new TeamLabApiContractException("topology_invalid", "仅支持 WireGuard 访问授权。", 422);
        await RequireAsync(runtimeId, TeamLabRuntimePermission.LifecycleManage, cancellationToken);
        var grant = await access.CreateAsync(runtimeId, cancellationToken);
        return Created($"/api/admin/teamlab/runtimes/{runtimeId:D}/access-grants/{grant.Id:D}",
            grant with { ConfigurationDownloadUrl = AdminDownloadUrl(grant.ConfigurationDownloadUrl) });
    }

    [HttpGet("{runtimeId:guid}/access-grants")]
    public async Task<IReadOnlyList<TeamLabAccessGrantModel>> ListAccessGrants(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.StateRead, cancellationToken);
        return (await access.ListAsync(runtimeId, cancellationToken))
            .Select(item => item with { ConfigurationDownloadUrl = AdminDownloadUrl(item.ConfigurationDownloadUrl) })
            .ToArray();
    }

    [HttpDelete("{runtimeId:guid}/access-grants/{grantId:guid}")]
    public async Task<IActionResult> RevokeAccessGrant(
        Guid runtimeId,
        Guid grantId,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.LifecycleManage, cancellationToken);
        await access.RevokeAsync(runtimeId, grantId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{runtimeId:guid}/access-grants/{grantId:guid}/download")]
    public async Task<IActionResult> DownloadAccessGrant(
        Guid runtimeId,
        Guid grantId,
        [FromQuery, Required] string token,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.LifecycleManage, cancellationToken);
        var result = await access.ConsumeConfigurationAsync(runtimeId, grantId, token, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(result.Configuration),
            "application/x-wireguard-profile", result.FileName);
    }

    [HttpPost("{runtimeId:guid}/captures")]
    public async Task<ActionResult<TeamLabCaptureModel>> StartCapture(
        Guid runtimeId,
        CreateTeamLabCaptureModel model,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.LifecycleManage, cancellationToken);
        var capture = await traffic.StartCaptureAsync(runtimeId, model, cancellationToken);
        return Created($"/api/admin/teamlab/runtimes/{runtimeId:D}/captures/{capture.Id:D}", capture);
    }

    [HttpGet("{runtimeId:guid}/captures/{captureId:guid}")]
    public async Task<TeamLabCaptureModel> GetCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.MetadataRead, cancellationToken);
        return await traffic.GetCaptureAsync(runtimeId, captureId, cancellationToken);
    }

    [HttpPost("{runtimeId:guid}/captures/{captureId:guid}/stop")]
    public async Task<TeamLabCaptureModel> StopCapture(
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.LifecycleManage, cancellationToken);
        return await traffic.StopCaptureAsync(runtimeId, captureId, cancellationToken);
    }

    [HttpGet("{runtimeId:guid}/captures/{captureId:guid}/download")]
    public async Task<IActionResult> DownloadCapture(Guid runtimeId, Guid captureId, CancellationToken cancellationToken)
    {
        await RequireAsync(runtimeId, TeamLabRuntimePermission.MetadataRead, cancellationToken);
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

    private async Task RequireAsync(
        Guid runtimeId,
        TeamLabRuntimePermission required,
        CancellationToken cancellationToken)
    {
        var actor = await users.GetUserAsync(User)
            ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        await authorization.RequirePermissionAsync(
            runtimeId, actor.Id, actor.Role >= Role.Admin, required, cancellationToken);
    }

    private static string? AdminDownloadUrl(string? value) =>
        value?.Replace("/api/open/v1/teamlab/runtimes/", "/api/admin/teamlab/runtimes/", StringComparison.Ordinal);

    private async Task<UserInfo> ActorAsync() =>
        await users.GetUserAsync(User)
        ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
}
