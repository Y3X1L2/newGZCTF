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
[OpenApiTags("TeamLab - Device packages")]
[Route("api/open/v1/teamlab/device-packages")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabDevicePackagesController(
    TeamLabDevicePackageService packages) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabDevicePackagesRead)]
    [OpenApiOperation("列出设备包", "按名称过滤返回不可变设备包版本，使用稳定 cursor 分页")]
    [ProducesResponseType(typeof(TeamLabDevicePackagePageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabDevicePackagePageModel> List(
        [FromQuery] string? name,
        [FromQuery] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        await packages.ListAsync(name, after, limit, cancellationToken);

    [HttpGet("{packageId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabDevicePackagesRead)]
    [OpenApiOperation("获取设备包版本", "返回版本、制品引用、资源需求、参数 schema 与能力声明")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabDevicePackageModel> Get(Guid packageId, CancellationToken cancellationToken) =>
        await packages.GetAsync(packageId, cancellationToken);
}
