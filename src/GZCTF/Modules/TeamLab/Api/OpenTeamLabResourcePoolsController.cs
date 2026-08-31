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
[OpenApiTags("TeamLab - Resource pools")]
[Route("api/open/v1/teamlab/resource-pools")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class OpenTeamLabResourcePoolsController(
    TeamLabResourcePoolService pools) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabResourcePoolsRead)]
    [OpenApiOperation("获取资源池快照", "返回计算节点与模板的只读容量/状态投影，不暴露执行面地址")]
    [ProducesResponseType(typeof(TeamLabResourcePoolSnapshotModel), StatusCodes.Status200OK)]
    public async Task<TeamLabResourcePoolSnapshotModel> Snapshot(CancellationToken cancellationToken) =>
        await pools.GetSnapshotAsync(cancellationToken);

    [HttpGet("node-cache")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabResourcePoolsRead)]
    [OpenApiOperation("列出节点制品缓存", "按节点与模板返回分发状态、阶段与活动用途引用计数")]
    [ProducesResponseType(typeof(TeamLabNodeCachePageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabNodeCachePageModel> NodeCache(
        [FromQuery] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        await pools.ListNodeCacheAsync(after, limit, cancellationToken);
}
