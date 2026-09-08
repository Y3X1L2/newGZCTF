using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeDifferenceService(AppDbContext context, TeamLabAuthorizationService authorization,
    RuntimeFactReconciliationService reconciliation, TeamLabAssetControlService controls)
{
    public async Task<RuntimeDifferencePreview> PreviewAsync(Guid runtimeId, Guid actorId, bool administrator, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.StateRead, token);
        var id = await context.TeamLabRuntimes.AsNoTracking().Where(item => item.PublicId == runtimeId)
            .Select(item => item.Id).SingleAsync(token);
        var preview = await reconciliation.PreviewTeamLabAsync(id, token);
        var items = new List<RuntimeResourceDifference>();
        foreach (var item in preview.Items)
        {
            var allowed = item.SuggestedAction is not null && item.AssetId is { } assetId &&
                (await controls.AvailabilityAsync(runtimeId, assetId, actorId, administrator, token)).Allowed;
            items.Add(allowed ? item : item with { SuggestedAction = null });
        }
        return preview with { Items = items };
    }

    public async Task<TeamLabQueueTicketResult> RepairAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator,
        TeamLabAssetControlCommand command, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.LifecycleManage, token);
        var preview = await PreviewAsync(runtimeId, actorId, administrator, token);
        var item = preview.Items.SingleOrDefault(item => item.AssetId == assetId);
        if (preview.Generation != command.Generation || preview.OperationInProgress || item?.SuggestedAction is null ||
            item.SuggestedAction != command.Action)
            throw new TeamLabApiContractException("reconciliation.preview_changed", "现场状态已变化，请重新检查差异后操作。", 409);
        return await controls.EnqueueAsync(runtimeId, assetId, actorId, administrator, command, token);
    }
}
