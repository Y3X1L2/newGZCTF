using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

internal static class TeamLabAssetControlGuard
{
    public static async Task<bool> IsActiveAsync(AppDbContext context, TeamLabRuntimeOperationPayloadProtector? protector,
        int runtimeId, int assetId, CancellationToken token)
    {
        var payload = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.TeamLabRuntimeId == runtimeId && item.Operation == RuntimeOperationKind.AssetControl &&
                (item.Status == DeploymentQueueTicketStatus.Pending || item.Status == DeploymentQueueTicketStatus.Scheduling ||
                 item.Status == DeploymentQueueTicketStatus.Scheduled || item.Status == DeploymentQueueTicketStatus.Running))
            .Select(item => item.ProtectedPayload).FirstOrDefaultAsync(token);
        return payload is not null && (protector is null || protector.Unprotect(payload).AssetControl?.AssetId == assetId);
    }
}
