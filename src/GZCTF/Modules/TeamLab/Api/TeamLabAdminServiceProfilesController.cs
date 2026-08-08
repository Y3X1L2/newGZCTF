using System.ComponentModel.DataAnnotations;
using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/service-profiles")]
public sealed class TeamLabAdminServiceProfilesController(
    TeamLabServiceProfileCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<TeamLabServiceProfilePageModel> List(
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        catalog.ListAsync(after, limit, cancellationToken);

    [HttpGet("{profileId:guid}")]
    public Task<TeamLabServiceProfileDetailModel> Get(
        Guid profileId,
        [FromQuery, Range(1, int.MaxValue)] int? version = null,
        CancellationToken cancellationToken = default) =>
        catalog.GetAsync(profileId, version, cancellationToken);
}
