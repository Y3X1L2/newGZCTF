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
[OpenApiTags("TeamLab - Runtime status")]
[Route("api/open/v1/teamlab/runtimes/{runtimeId:guid}")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
public sealed class OpenTeamLabRuntimeStatusController(
    TeamLabDeviceObservationService deviceHealth,
    TeamLabRuntimeDifferenceService differences) : ControllerBase
{
    [HttpGet("device-health")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [OpenApiOperation("查询设备健康", "返回当前代设备模板资产的健康检查结果和下一次检查时间。")]
    [ProducesResponseType(typeof(IReadOnlyList<OpenTeamLabDeviceHealthModel>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<OpenTeamLabDeviceHealthModel>> DeviceHealth(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        Response.Headers.CacheControl = "no-store";
        return (await deviceHealth.ReadApiAsync(
                runtimeId, actor.TokenId, IsAdministrator(), cancellationToken))
            .Select(item => item.ToOpen())
            .ToArray();
    }

    [HttpGet("status-check")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [OpenApiOperation("检查运行状态", "对比当前代资产与节点现场状态；修复时使用返回的 suggestedAction 调用单资产控制接口。")]
    [ProducesResponseType(typeof(OpenTeamLabRuntimeStatusCheckModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabRuntimeStatusCheckModel> StatusCheck(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        Response.Headers.CacheControl = "no-store";
        return (await differences.PreviewApiAsync(
            runtimeId, actor.TokenId, IsAdministrator(), cancellationToken)).ToOpen();
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份验证。", 401);
    }

    private bool IsAdministrator() => User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
        ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
        type == "teamlab-scope" && id == "*");
}
