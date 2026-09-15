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

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabDevicePackagesWrite)]
    [OpenApiOperation("登记设备模板", "登记由外部流水线制作的设备模板及其制品、资源和能力声明")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(
        OpenRegisterTeamLabDevicePackageModel model,
        CancellationToken cancellationToken)
    {
        RequireWildcardGrant();
        var package = await packages.RegisterAsync(model.ToInternal(), cancellationToken);
        return Created($"/api/open/v1/teamlab/device-packages/{package.Id:D}", package);
    }

    [HttpPut("{packageId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabDevicePackagesWrite)]
    [OpenApiOperation("修改设备模板", "替换设备模板登记信息；名称与版本组合仍保持唯一")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabDevicePackageModel> Update(
        Guid packageId,
        OpenUpdateTeamLabDevicePackageModel model,
        CancellationToken cancellationToken)
    {
        RequireWildcardGrant();
        return await packages.UpdateAsync(packageId, model.ToInternal(), cancellationToken);
    }

    [HttpPost("{packageId:guid}/enable")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabDevicePackagesWrite)]
    [OpenApiOperation("启用设备模板", "允许新发布和运行使用该设备模板")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabDevicePackageModel> Enable(Guid packageId, CancellationToken cancellationToken)
    {
        RequireWildcardGrant();
        return await packages.SetEnabledAsync(packageId, true, cancellationToken);
    }

    [HttpPost("{packageId:guid}/disable")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabDevicePackagesWrite)]
    [OpenApiOperation("停用设备模板", "阻止新发布和运行使用该设备模板，既有引用保持可追溯")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status200OK)]
    public async Task<TeamLabDevicePackageModel> Disable(Guid packageId, CancellationToken cancellationToken)
    {
        RequireWildcardGrant();
        return await packages.SetEnabledAsync(packageId, false, cancellationToken);
    }

    [HttpPost("{packageId:guid}/archive")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabDevicePackagesWrite)]
    [OpenApiOperation("归档设备模板", "归档并停用设备模板；归档后不再出现在公开目录")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid packageId, CancellationToken cancellationToken)
    {
        RequireWildcardGrant();
        await packages.ArchiveAsync(packageId, cancellationToken);
        return NoContent();
    }

    private void RequireWildcardGrant()
    {
        var granted = User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
            ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
            type == "teamlab-scope" && id == "*");
        if (!granted)
            throw new TeamLabApiContractException(
                "insufficient_permission", "设备模板写入需要 teamlab-scope:* 资源授权", 403);
    }
}
