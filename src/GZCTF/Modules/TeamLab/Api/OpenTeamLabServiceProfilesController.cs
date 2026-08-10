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
[OpenApiTags("TeamLab - Service Profiles")]
[OpenApiTag("TeamLab - Service Profiles", Description = "Browse the bootstrap service-profile catalog used by TeamLab topologies.")]
[Route("api/open/v1/teamlab/service-profiles")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabServiceProfilesController(
    TeamLabServiceProfileCatalogService catalog) : ControllerBase
{
    [HttpGet]
    [OpenApiOperation("列出服务目录", "返回 cursor 分页的已发布服务目录条目及其参数概览。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabServiceProfilePageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabServiceProfilePageModel> List(
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        await catalog.ListAsync(after, limit, cancellationToken);

    [HttpGet("{profileId:guid}")]
    [OpenApiOperation("获取服务目录条目", "返回单个服务目录条目的参数 schema、默认值与执行特性；绝不包含脚本内容或密钥值。可通过 version 查询历史版本 schema。")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabTopologiesRead)]
    [ProducesResponseType(typeof(TeamLabServiceProfileDetailModel), StatusCodes.Status200OK)]
    public async Task<TeamLabServiceProfileDetailModel> Get(
        Guid profileId,
        [FromQuery, Range(1, int.MaxValue)] int? version = null,
        CancellationToken cancellationToken = default) =>
        await catalog.GetAsync(profileId, version, cancellationToken);
}
