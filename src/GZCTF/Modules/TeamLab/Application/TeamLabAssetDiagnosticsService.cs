using GZCTF.Models;
using GZCTF.Modules.Audit.Domain;
using GZCTF.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabAssetDiagnosticsGateway
{
    Task<TeamLabVmDiagnostics> ReadVmAsync(Guid nodeId, TeamLabVmDiagnosticsRequest request, CancellationToken token);
    Task<TeamLabContainerDiagnostics> ReadAsync(Guid nodeId, TeamLabContainerDiagnosticsRequest request, CancellationToken token);
}

public sealed class TeamLabAssetDiagnosticsService(AppDbContext context, TeamLabAuthorizationService authorization,
    ITeamLabAssetDiagnosticsGateway gateway, TeamLabEventRecorder events)
{
    public async Task<TeamLabVmDiagnostics> ReadVmAsync(Guid runtimeId, int assetId, Guid actorId,
        bool administrator, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator,
            TeamLabRuntimePermission.RemoteSessionOperate, token);
        var asset = await context.TeamLabRuntimeAssets.AsNoTracking().Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.Id == assetId && item.Runtime.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_asset_not_found", "未找到运行资产。", 404);
        if (asset.Kind != TeamLabResourceKind.Vm)
            throw new TeamLabApiContractException("diagnostics.unsupported", "此诊断入口仅支持 VM 资产。", 422);
        if (asset.WorkerNodeId is not { } node || string.IsNullOrWhiteSpace(asset.RuntimeResourceId) ||
            !Guid.TryParse(asset.NativeIdentity, out var nativeId) || nativeId == Guid.Empty)
            throw new TeamLabApiContractException("diagnostics.unavailable", "VM 尚未绑定完整的 libvirt 执行身份。", 409);
        var result = await gateway.ReadVmAsync(node, new(asset.RuntimeResourceId, asset.Runtime.Generation, nativeId), token);
        if (result.NativeId != nativeId || !await context.TeamLabRuntimeAssets.AnyAsync(item => item.Id == assetId &&
                item.Runtime.Generation == asset.Runtime.Generation && item.WorkerNodeId == node &&
                item.NativeIdentity == asset.NativeIdentity && item.RuntimeResourceId == asset.RuntimeResourceId, token))
            throw new TeamLabApiContractException("diagnostics.stale_generation", "采集期间 VM 绑定已变化，请刷新。", 409);
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.RemoteSessionOperate, token);
        events.Record(asset.Runtime, "asset-diagnostics", TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.AssetDiagnosticsRead, OperationalEventOutcome.Succeeded,
            "读取 VM 资产电源状态", workerNodeId: node, detail: new Dictionary<string, object?>
            { ["assetId"] = asset.Id, ["actorUserId"] = actorId, ["generation"] = asset.Runtime.Generation });
        await context.SaveChangesAsync(token);
        return result;
    }

    public async Task<TeamLabContainerDiagnostics> ReadAsync(Guid runtimeId, int assetId, Guid actorId,
        bool administrator, int tail, CancellationToken token)
    {
        if (tail is < 1 or > 1000)
            throw new TeamLabApiContractException("diagnostics.invalid_tail", "日志行数需为 1-1000。", 400);
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator,
            TeamLabRuntimePermission.RemoteSessionOperate, token);
        var asset = await context.TeamLabRuntimeAssets.AsNoTracking().Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.Id == assetId && item.Runtime.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_asset_not_found", "未找到运行资产。", 404);
        if (asset.Kind != TeamLabResourceKind.Docker)
            throw new TeamLabApiContractException("diagnostics.unsupported", "此诊断入口仅支持 Docker 资产。", 422);
        if (asset.WorkerNodeId is not { } node || string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
            throw new TeamLabApiContractException("diagnostics.unavailable", "资产尚未绑定可查询的执行资源。", 409);
        var result = await gateway.ReadAsync(node, new(asset.RuntimeId, asset.Runtime.Generation, asset.RuntimeResourceId, tail), token);
        if (!await context.TeamLabRuntimeAssets.AnyAsync(item => item.Id == assetId &&
                item.Runtime.Generation == asset.Runtime.Generation && item.WorkerNodeId == node &&
                item.RuntimeResourceId == asset.RuntimeResourceId, token))
            throw new TeamLabApiContractException("diagnostics.stale_generation", "采集期间资产已重建，请重新刷新。", 409);
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator,
            TeamLabRuntimePermission.RemoteSessionOperate, token);
        events.Record(asset.Runtime, "asset-diagnostics", TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.AssetDiagnosticsRead, OperationalEventOutcome.Succeeded,
            "读取容器资产诊断", workerNodeId: node, detail: new Dictionary<string, object?>
            {
                ["assetId"] = asset.Id,
                ["actorUserId"] = actorId,
                ["generation"] = asset.Runtime.Generation,
                ["tail"] = tail,
                ["truncated"] = result.Truncated,
                ["logsAvailable"] = result.LogsError is null
            });
        await context.SaveChangesAsync(token);
        return result;
    }
}
