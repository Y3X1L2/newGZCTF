using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Topologies")]
[OpenApiTag("TeamLab - Topologies", Description = "Design, validate, publish, and plan multi-segment TeamLab network topologies.")]
[Route("api/open/v1/teamlab")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabTopologiesController(
    ITeamLabTopologyApplicationService topologies,
    TeamLabRuntimeOperationApplicationService operations) : ControllerBase
{
    [HttpGet("capabilities")]
    [OpenApiOperation("Get TeamLab capabilities", "Returns the topology schema and feature capabilities supported by this platform version.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabCapabilitiesModel), StatusCodes.Status200OK)]
    public ActionResult<TeamLabCapabilitiesModel> Capabilities() => Ok(topologies.GetCapabilities());

    [HttpPost("topologies")]
    [OpenApiOperation("Create a topology", "Queues creation of a reusable TeamLab topology draft.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(
        OpenCreateTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var result = await operations.SubmitTopologyCreateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, model.ToInternal(), cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("topologies")]
    [OpenApiOperation("List topologies", "Returns a cursor-paginated list of topologies visible to the current API token owner.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabTopologyPageModel), StatusCodes.Status200OK)]
    public Task<OpenTeamLabTopologyPageModel> List(
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        topologies.ListPageAsync(ActorUserId(), IsAdministrator(), limit, after, cancellationToken);

    [HttpGet("topologies/{topologyId:guid}")]
    [OpenApiOperation("Get a topology", "Returns the complete editable topology definition.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabTopologyDetailModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabTopologyDetailModel> Get(Guid topologyId, CancellationToken cancellationToken) =>
        (await topologies.GetAsync(topologyId, ActorUserId(), IsAdministrator(), cancellationToken)).ToOpen();

    [HttpPut("topologies/{topologyId:guid}")]
    [OpenApiOperation("Update a topology", "Queues replacement of the editable topology definition.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Update(
        Guid topologyId,
        OpenUpdateTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var result = await operations.SubmitTopologyUpdateAsync(
            actor.TokenId, actor.UserId, idempotencyKey, topologyId, model.ToInternal(), cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpDelete("topologies/{topologyId:guid}")]
    [OpenApiOperation("Delete a topology", "Queues deletion of a topology that is no longer in use.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(
        Guid topologyId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var result = await operations.SubmitTopologyDeleteAsync(
            actor.TokenId, actor.UserId, idempotencyKey, topologyId, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpPost("topologies/{topologyId:guid}/validate")]
    [OpenApiOperation("Validate a topology", "Validates topology structure, addressing, connectivity, assets, and deployment constraints without publishing it.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabValidationResultModel), StatusCodes.Status200OK)]
    public Task<TeamLabValidationResultModel> Validate(Guid topologyId, CancellationToken cancellationToken) =>
        topologies.ValidateAsync(topologyId, ActorUserId(), IsAdministrator(), cancellationToken);

    [HttpPost("topologies/{topologyId:guid}/releases")]
    [OpenApiOperation("Publish a topology release", "Validates and queues creation of an immutable topology release for runtime deployment.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Publish(
        Guid topologyId,
        PublishTeamLabTopologyModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var result = await operations.SubmitTopologyPublishAsync(
            actor.TokenId, actor.UserId, idempotencyKey, topologyId, model, cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpGet("topologies/{topologyId:guid}/releases")]
    [OpenApiOperation("List topology releases", "Returns immutable releases for a topology using cursor pagination.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabReleasePageModel), StatusCodes.Status200OK)]
    public Task<OpenTeamLabReleasePageModel> ListReleases(
        Guid topologyId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        topologies.ListReleasesPageAsync(
            topologyId, ActorUserId(), IsAdministrator(), limit, after, cancellationToken);

    [HttpGet("topologies/{topologyId:guid}/releases/{releaseId:guid}")]
    [OpenApiOperation("Get a topology release", "Returns one immutable topology release and its compiled definition.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(OpenTeamLabReleaseModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabReleaseModel> GetRelease(
        Guid topologyId,
        Guid releaseId,
        CancellationToken cancellationToken) =>
        (await topologies.GetReleaseAsync(
            topologyId, releaseId, ActorUserId(), IsAdministrator(), cancellationToken)).ToOpen();

    [HttpPost("topologies/{topologyId:guid}/releases/{releaseId:guid}/plan")]
    [OpenApiOperation("Plan runtime placement", "Builds a deployment plan for the release without creating runtime resources.")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabPlanModel), StatusCodes.Status200OK)]
    public Task<TeamLabPlanModel> Plan(Guid topologyId, Guid releaseId, CancellationToken cancellationToken) =>
        topologies.PlanAsync(topologyId, releaseId, ActorUserId(), IsAdministrator(), cancellationToken);

    private Guid ActorUserId() => Actor().UserId;

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "Authentication is required.", 401);
    }

    private AcceptedResult AcceptedOperation(GZCTF.Modules.Audit.Application.IdempotencyBeginResult result)
    {
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private bool IsAdministrator() => User.IsInRole(nameof(Role.Admin));
}
