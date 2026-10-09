using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

internal static class TeamLabReleaseLifecycle
{
    internal static async Task LockAsync(AppDbContext context, Guid releaseId, CancellationToken token)
    {
        if (context.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL") return;
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Release lifecycle locking requires a transaction.");
        var key = $"teamlab:release-lifecycle:{releaseId:D}";
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
    }

    internal static async Task RequireStartableAsync(AppDbContext context, Guid releaseId, CancellationToken token)
    {
        var archived = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(release => release.Id == releaseId)
            .Select(release => (bool?)release.IsArchived).SingleOrDefaultAsync(token)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        if (archived)
            throw new TeamLabApiContractException("release_archived", "该拓扑版本已归档，无法创建、重建或预热。", 409);
    }

    internal static async Task RequireNoActiveResetAsync(
        AppDbContext context, Guid releaseId, TeamLabRuntimeOperationPayloadProtector? protector, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var tickets = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Kind == DeploymentQueueKind.TeamLabRuntime &&
                            ticket.Operation == RuntimeOperationKind.Reset &&
                            (ticket.Status == DeploymentQueueTicketStatus.Pending ||
                             ticket.Status == DeploymentQueueTicketStatus.Scheduling ||
                             ticket.Status == DeploymentQueueTicketStatus.Scheduled ||
                             ticket.Status == DeploymentQueueTicketStatus.Running ||
                             ticket.ClaimOwner != null && ticket.ClaimExpiresAt > now))
            .Select(ticket => new
            {
                ticket.ProtectedPayload,
                SourceReleaseId = context.TeamLabRuntimes
                    .Where(runtime => runtime.Id == ticket.TeamLabRuntimeId)
                    .Select(runtime => (Guid?)runtime.TopologyReleaseId).FirstOrDefault()
            }).ToArrayAsync(token);
        foreach (var ticket in tickets)
        {
            if (ticket.SourceReleaseId == releaseId)
                throw ResetBusy();
            // The unified ticket already carries the protected target release. Decode only
            // in this trusted application boundary; never emit its overlays or ciphertext.
            if (protector is null || string.IsNullOrWhiteSpace(ticket.ProtectedPayload))
                throw ResetBusy(); // Unknown target must not be retired underneath a live reset.
            try
            {
                if (protector.Unprotect(ticket.ProtectedPayload).Reset?.ReleaseId == releaseId)
                    throw ResetBusy();
            }
            catch (ApiOperationTerminalException)
            {
                throw ResetBusy();
            }
        }
    }

    private static TeamLabApiContractException ResetBusy() => new(
        "release_reset_in_progress", "存在进行中的运行环境重置，请完成或取消后再归档版本。", 409);
}
