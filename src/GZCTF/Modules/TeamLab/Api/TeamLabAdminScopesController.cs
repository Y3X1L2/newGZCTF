using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireAdmin]
[ApiController]
[Route("api/admin/teamlab/scopes")]
public sealed class TeamLabAdminScopesController(TeamLabControlScopeService scopes) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TeamLabControlScopeModel>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<TeamLabControlScopeModel>> List(CancellationToken cancellationToken)
        => await scopes.ListGrantedAsync([], administrator: true, cancellationToken);

    [HttpPost("{scopeId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid scopeId, CancellationToken cancellationToken)
    {
        await scopes.ArchiveAsync(scopeId, cancellationToken);
        return NoContent();
    }
}
