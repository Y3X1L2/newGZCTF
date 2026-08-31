using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Control scopes")]
[Route("api/open/v1/teamlab/scopes")]
public sealed class OpenTeamLabScopesController(
    TeamLabControlScopeService scopes) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [OpenApiOperation("List TeamLab control scopes", "Lists only scopes granted to the current token, unless the token carries a wildcard teamlab-scope grant.")]
    public async Task<IReadOnlyList<TeamLabControlScopeModel>> List(CancellationToken cancellationToken)
    {
        var administrator = User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
            ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
            type == "teamlab-scope" && id == "*");
        var granted = User.FindAll(ApiTokenClaimTypes.Resource)
            .Select(claim => ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id)
                ? (Type: type, Id: id) : (Type: string.Empty, Id: string.Empty))
            .Where(grant => grant.Type == "teamlab-scope")
            .Select(grant => Guid.TryParse(grant.Id, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();
        return await scopes.ListGrantedAsync(granted, administrator, cancellationToken);
    }

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(typeof(TeamLabControlScopeModel), StatusCodes.Status201Created)]
    [OpenApiOperation("Create a TeamLab control scope", "Creates an external resource boundary. Only API tokens created by administrators may create scopes.")]
    public async Task<ActionResult<TeamLabControlScopeModel>> Create(
        CreateTeamLabControlScopeModel model,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.CreateAsync(model, User.IsInRole(nameof(Role.Admin)), cancellationToken);
        return Created($"/api/open/v1/teamlab/scopes/{scope.Id:D}", scope);
    }

    [HttpPost("{scopeId:guid}/archive")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [OpenApiOperation("Archive a TeamLab control scope", "Archived scopes stay readable and drainable but accept no new writes. Idempotent; only administrator-created tokens may archive.")]
    public async Task<IActionResult> Archive(Guid scopeId, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(nameof(Role.Admin)))
            throw new TeamLabApiContractException("insufficient_permission", "仅管理员可以归档 TeamLab 控制范围", 403);
        await scopes.ArchiveAsync(scopeId, cancellationToken);
        return NoContent();
    }
}
