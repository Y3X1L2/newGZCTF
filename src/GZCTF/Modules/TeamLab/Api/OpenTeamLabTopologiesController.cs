using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/teamlab")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenTeamLabTopologiesController(ITeamLabTopologyApplicationService topologies) : ControllerBase
{
    [HttpGet("capabilities")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    public ActionResult<TeamLabCapabilitiesModel> Capabilities() => Ok(topologies.GetCapabilities());

    [HttpPost("topologies")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    public async Task<ActionResult<TeamLabTopologyDetailModel>> Create(
        CreateTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = idempotencyKey;
        var result = await topologies.CreateAsync(model, ActorUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { topologyId = result.Id }, result);
    }

    [HttpGet("topologies")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    public Task<IReadOnlyList<TeamLabTopologySummaryModel>> List(CancellationToken cancellationToken) =>
        topologies.ListAsync(ActorUserId(), IsAdministrator(), cancellationToken);

    [HttpGet("topologies/{topologyId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    public Task<TeamLabTopologyDetailModel> Get(Guid topologyId, CancellationToken cancellationToken) =>
        topologies.GetAsync(topologyId, ActorUserId(), IsAdministrator(), cancellationToken);

    [HttpPut("topologies/{topologyId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    public Task<TeamLabTopologyDetailModel> Update(
        Guid topologyId,
        UpdateTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = idempotencyKey;
        return topologies.UpdateAsync(topologyId, model, ActorUserId(), IsAdministrator(), cancellationToken);
    }

    [HttpDelete("topologies/{topologyId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    public async Task<IActionResult> Delete(
        Guid topologyId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = idempotencyKey;
        await topologies.DeleteAsync(topologyId, ActorUserId(), IsAdministrator(), cancellationToken);
        return NoContent();
    }

    [HttpPost("topologies/{topologyId:guid}/validate")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    public Task<TeamLabValidationResultModel> Validate(Guid topologyId, CancellationToken cancellationToken) =>
        topologies.ValidateAsync(topologyId, ActorUserId(), IsAdministrator(), cancellationToken);

    [HttpPost("topologies/{topologyId:guid}/releases")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    public async Task<ActionResult<TeamLabReleaseModel>> Publish(
        Guid topologyId,
        PublishTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = idempotencyKey;
        var release = await topologies.PublishAsync(
            topologyId, model.Revision, ActorUserId(), IsAdministrator(), cancellationToken);
        return CreatedAtAction(nameof(GetRelease), new { topologyId, releaseId = release.Id }, release);
    }

    [HttpGet("topologies/{topologyId:guid}/releases")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    public Task<IReadOnlyList<TeamLabReleaseModel>> ListReleases(Guid topologyId, CancellationToken cancellationToken) =>
        topologies.ListReleasesAsync(topologyId, ActorUserId(), IsAdministrator(), cancellationToken);

    [HttpGet("topologies/{topologyId:guid}/releases/{releaseId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    public Task<TeamLabReleaseModel> GetRelease(Guid topologyId, Guid releaseId, CancellationToken cancellationToken) =>
        topologies.GetReleaseAsync(topologyId, releaseId, ActorUserId(), IsAdministrator(), cancellationToken);

    [HttpPost("topologies/{topologyId:guid}/releases/{releaseId:guid}/plan")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    public Task<TeamLabPlanModel> Plan(Guid topologyId, Guid releaseId, CancellationToken cancellationToken) =>
        topologies.PlanAsync(topologyId, releaseId, ActorUserId(), IsAdministrator(), cancellationToken);

    private Guid ActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);

    private bool IsAdministrator() => User.IsInRole(nameof(Role.Admin));
}
