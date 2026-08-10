using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab")]
public sealed class TeamLabAdminTopologyController(
    ITeamLabTopologyApplicationService topologies,
    TeamLabAdminQueryService queries,
    TeamLabReleaseImagePreparationService imagePreparation,
    UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet("capabilities")]
    public TeamLabCapabilitiesModel Capabilities() => topologies.GetCapabilities();

    [HttpGet("topologies")]
    public async Task<TeamLabAdminScenePageModel> List(
        [FromQuery] string? search = null,
        [FromQuery] string? owner = null,
        [FromQuery] Guid? ownerId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? after = null,
        [FromQuery] int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync();
        return await queries.ListScenesAsync(
            actor.Id,
            actor.Role >= Role.Admin,
            search,
            owner,
            ownerId,
            status,
            after,
            limit,
            cancellationToken);
    }

    [HttpPost("topologies")]
    public async Task<ActionResult<TeamLabTopologyDetailModel>> Create(
        CreateTeamLabTopologyModel model,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        var topology = await topologies.CreateDraftAsync(model, actor.Id, cancellationToken);
        return Created($"/api/admin/teamlab/topologies/{topology.Id:D}", topology);
    }

    [HttpGet("topologies/{topologyId:guid}")]
    public async Task<TeamLabTopologyDetailModel> Get(Guid topologyId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await topologies.GetAsync(topologyId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpPut("topologies/{topologyId:guid}")]
    public async Task<TeamLabTopologyDetailModel> Update(
        Guid topologyId,
        UpdateTeamLabTopologyModel model,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await topologies.UpdateDraftAsync(topologyId, model, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpDelete("topologies/{topologyId:guid}")]
    public async Task<IActionResult> Delete(Guid topologyId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        await topologies.DeleteAsync(topologyId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
        return NoContent();
    }

    [HttpPost("topologies/{topologyId:guid}/validate")]
    public async Task<TeamLabValidationResultModel> Validate(Guid topologyId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await topologies.ValidateAsync(topologyId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpPost("topologies/{topologyId:guid}/releases")]
    public async Task<TeamLabReleaseModel> Publish(
        Guid topologyId,
        PublishTeamLabTopologyModel model,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await topologies.PublishAsync(
            topologyId,
            model.Revision,
            actor.Id,
            actor.Role >= Role.Admin,
            cancellationToken);
    }

    [HttpGet("topologies/{topologyId:guid}/releases")]
    public async Task<IReadOnlyList<TeamLabReleaseModel>> Releases(Guid topologyId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await topologies.ListReleasesAsync(topologyId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpPost("topologies/{topologyId:guid}/releases/{releaseId:guid}/plan")]
    public async Task<TeamLabPlanModel> Plan(Guid topologyId, Guid releaseId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await topologies.PlanAsync(topologyId, releaseId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpGet("topologies/{topologyId:guid}/releases/{releaseId:guid}/readiness")]
    public async Task<TeamLabAdminReleaseReadinessModel> Readiness(
        Guid topologyId,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        return await queries.GetReleaseReadinessAsync(
            topologyId, releaseId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    [HttpPost("topologies/{topologyId:guid}/releases/{releaseId:guid}/images/prepare")]
    public async Task<TeamLabAdminReleaseReadinessModel> PrepareImages(
        Guid topologyId,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync();
        await queries.GetReleaseReadinessAsync(
            topologyId, releaseId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
        await imagePreparation.QueueAsync(releaseId, cancellationToken);
        return await queries.GetReleaseReadinessAsync(
            topologyId, releaseId, actor.Id, actor.Role >= Role.Admin, cancellationToken);
    }

    private async Task<UserInfo> ActorAsync() =>
        await users.GetUserAsync(User) ?? throw new UnauthorizedAccessException("Login required.");
}

