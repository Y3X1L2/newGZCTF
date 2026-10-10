using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Application;

public sealed class TeamLabRecordReferenceQuery(AppDbContext context) : ITeamLabRecordReferenceQuery, ITeamLabCreationHistory
{
    private const string ReceiptKind = "teamlab.runtime.creation.retired.v1";
    private const string ReceiptRoute = "teamlab:runtime-create-retired";

    public async Task LockAsync(Guid ownerId, string key, CancellationToken token)
    {
        if (!context.Database.IsNpgsql()) return;
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Creation history locking requires a transaction.");
        var identity = $"teamlab:runtime-create:{ownerId:D}:{key}";
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))", token);
    }

    public Task<string?> GetRetiredRequestHashAsync(Guid ownerId, string key, CancellationToken token) =>
        context.ApiOperations.AsNoTracking().Where(item => item.Kind == ReceiptKind && item.ActorUserId == ownerId &&
            item.ApiTokenId == null && item.RouteKey == ReceiptRoute && item.IdempotencyKey == key)
            .Select(item => item.RequestHash).SingleOrDefaultAsync(token);

    public async Task RetireAsync(Guid ownerId, string key, string requestHash, Guid publicId, CancellationToken token)
    {
        await LockAsync(ownerId, key, token);
        if (await GetRetiredRequestHashAsync(ownerId, key, token) is not null) return;
        var now = DateTimeOffset.UtcNow;
        // A completed receipt, not another queue/job. No payload or guest definition is retained.
        context.ApiOperations.Add(new ApiOperation
        {
            Kind = ReceiptKind, Status = ApiOperationStatus.Succeeded, Stage = "deleted", ActorUserId = ownerId,
            RouteKey = ReceiptRoute, IdempotencyKey = key, RequestHash = requestHash,
            ResourceType = "teamlab-runtime-deleted", ResourceId = publicId.ToString("D"),
            CreatedAt = now, UpdatedAt = now, CompletedAt = now
        });
    }

    public async Task<bool> HasActiveRuntimeReferencesAsync(int runtimeId, Guid publicId, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var resourceId = publicId.ToString("D");
        return await context.DeploymentQueueTickets.AnyAsync(item => item.Kind == DeploymentQueueKind.TeamLabRuntime && item.TeamLabRuntimeId == runtimeId &&
            (item.Status == DeploymentQueueTicketStatus.Pending || item.Status == DeploymentQueueTicketStatus.Scheduling ||
             item.Status == DeploymentQueueTicketStatus.Scheduled || item.Status == DeploymentQueueTicketStatus.Running ||
             item.ClaimOwner != null && item.ClaimExpiresAt > now), token) ||
            await context.FleetCapacityReservations.AnyAsync(item => item.DeploymentQueueTicket.TeamLabRuntimeId == runtimeId &&
                (item.Status == CapacityReservationStatus.Active ||
                 item.Status == CapacityReservationStatus.Confirmed && item.ReleasedAt == null), token) ||
            await context.ImageDistributionReferences.AnyAsync(item => item.Kind == ImageDistributionReferenceKind.TeamLabRuntime && item.ResourceId == runtimeId, token) ||
            await context.ApiOperations.AnyAsync(item => item.ResourceType == "teamlab-runtime" && item.ResourceId == resourceId &&
                (item.Status == ApiOperationStatus.Pending || item.Status == ApiOperationStatus.Running), token);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveOperationIdsAsync(CancellationToken token) =>
        await context.ApiOperations.AsNoTracking().Where(item => item.Status == ApiOperationStatus.Pending || item.Status == ApiOperationStatus.Running)
            .Select(item => item.Id).ToArrayAsync(token);
}
