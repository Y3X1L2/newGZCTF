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
[OpenApiTags("TeamLab - Runtime operations")]
[OpenApiTag("TeamLab - Runtime operations", Description = "Publish runtime services and control individual runtime assets through the existing TeamLab execution plane.")]
[Route("api/open/v1/teamlab/runtimes/{runtimeId:guid}")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabRuntimeOperationsController(
    TeamLabServiceAccessService serviceAccess,
    TeamLabAssetControlService assetControl) : ControllerBase
{
    [HttpGet("service-access")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [OpenApiOperation("查询运行时服务映射", "返回运行时当前代及历史代的公网服务映射，不包含节点、Agent 或内部转发身份。")]
    [ProducesResponseType(typeof(IReadOnlyList<OpenTeamLabServiceAccessModel>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<OpenTeamLabServiceAccessModel>> ListServiceAccess(
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        return (await serviceAccess.ListForApiAsync(runtimeId, actor.TokenId, actor.UserId, cancellationToken))
            .Select(item => item.ToOpen())
            .ToArray();
    }

    [HttpPost("assets/{assetId:int}/service-access")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("创建运行时服务映射", "省略 publicPort 时自动分配公网端口；指定 publicPort 时尝试保留该端口。")]
    [ProducesResponseType(typeof(OpenTeamLabServiceAccessModel), StatusCodes.Status201Created)]
    public async Task<ActionResult<OpenTeamLabServiceAccessModel>> CreateServiceAccess(
        Guid runtimeId,
        int assetId,
        OpenCreateTeamLabServiceAccessModel model,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var result = (await serviceAccess.CreateForApiAsync(
            runtimeId, assetId, actor.TokenId, actor.UserId, model.ToInternal(), cancellationToken)).ToOpen();
        return Created($"/api/open/v1/teamlab/runtimes/{runtimeId:D}/service-access/{result.Id:D}", result);
    }

    [HttpDelete("service-access/{accessId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("撤销运行时服务映射", "撤销公网与节点转发规则并释放原端口租约；重复撤销返回相同终态。")]
    [ProducesResponseType(typeof(OpenTeamLabServiceAccessModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabServiceAccessModel> RemoveServiceAccess(
        Guid runtimeId,
        Guid accessId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        return (await serviceAccess.RemoveForApiAsync(
            runtimeId, accessId, actor.TokenId, actor.UserId, cancellationToken)).ToOpen();
    }

    [HttpGet("assets/{assetId:int}/control")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [OpenApiOperation("查询单资产控制能力", "检查当前代资产是否具备安全执行单资产生命周期命令所需的节点与不可变计划。")]
    [ProducesResponseType(typeof(OpenTeamLabAssetControlCapabilityModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabAssetControlCapabilityModel> AssetControlCapability(
        Guid runtimeId,
        int assetId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        return (await assetControl.AvailabilityForApiAsync(
            runtimeId, assetId, actor.UserId, actor.TokenId, cancellationToken)).ToOpen();
    }

    [HttpPost("assets/{assetId:int}/control")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesWrite)]
    [OpenApiOperation("提交单资产生命周期命令", "提交 start、stop、restart、rebuild、pause 或 resume，并返回统一部署队列的原始 ticket。")]
    [ProducesResponseType(typeof(OpenTeamLabAssetControlTicketModel), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<OpenTeamLabAssetControlTicketModel>> ControlAsset(
        Guid runtimeId,
        int assetId,
        OpenTeamLabAssetControlCommand model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        var ticket = await assetControl.EnqueueForApiAsync(
            runtimeId, assetId, actor.UserId, actor.TokenId, idempotencyKey,
            model.ToInternal(), cancellationToken);
        var result = new OpenTeamLabAssetControlTicketModel(ticket.TicketId);
        return Accepted(
            $"/api/open/v1/teamlab/runtimes/{runtimeId:D}/assets/{assetId}/control/{result.TicketId:D}",
            result);
    }

    [HttpGet("assets/{assetId:int}/control/{ticketId:guid}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRuntimesRead)]
    [OpenApiOperation("查询单资产生命周期任务", "直接读取原 DeploymentQueueTicket 的状态、阶段与安全错误码。")]
    [ProducesResponseType(typeof(OpenTeamLabAssetControlTaskModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabAssetControlTaskModel> GetAssetControlTask(
        Guid runtimeId,
        int assetId,
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        Response.Headers.CacheControl = "no-store";
        return (await assetControl.GetTaskForApiAsync(
            runtimeId, assetId, ticketId, actor.UserId, actor.TokenId, cancellationToken)).ToOpen();
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
    }
}
