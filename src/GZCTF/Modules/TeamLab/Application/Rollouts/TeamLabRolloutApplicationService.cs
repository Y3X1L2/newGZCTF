using System.Text;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application.Rollouts;

public sealed class TeamLabRolloutApplicationService(AppDbContext context) : ITeamLabRolloutApplicationService
{
    public async Task<TeamLabRolloutModel> EnsureAsync(
        Guid releaseId,
        Guid ownerUserId,
        Guid createdByUserId,
        string adapterKind,
        string externalReference,
        CancellationToken cancellationToken)
    {
        var rollout = await context.TeamLabRollouts
            .Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.ReleaseId == releaseId && item.AdapterKind == adapterKind &&
                                          item.ExternalReference == externalReference &&
                                          item.Status != TeamLabRolloutStatus.Completed, cancellationToken);
        if (rollout is null)
        {
            rollout = new TeamLabRollout
            {
                ReleaseId = releaseId,
                OwnerUserId = ownerUserId,
                CreatedByUserId = createdByUserId,
                AdapterKind = adapterKind,
                ExternalReference = externalReference
            };
            context.TeamLabRollouts.Add(rollout);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                context.Entry(rollout).State = EntityState.Detached;
                rollout = await context.TeamLabRollouts.Include(item => item.Targets)
                    .SingleAsync(item => item.ReleaseId == releaseId && item.AdapterKind == adapterKind &&
                                         item.ExternalReference == externalReference &&
                                         item.Status != TeamLabRolloutStatus.Completed, cancellationToken);
            }
        }
        return ToModel(rollout);
    }

    public async Task<TeamLabRolloutModel?> GetAsync(Guid rolloutId, CancellationToken cancellationToken)
    {
        var rollout = await context.TeamLabRollouts.AsNoTracking()
            .Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.PublicId == rolloutId, cancellationToken);
        return rollout is null ? null : ToModel(rollout);
    }

    public Task<TeamLabRolloutModel> RequestPreparationAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, rollout =>
        {
            if (rollout.DrainRequested)
                throw new TeamLabApiContractException("rollout_draining", "A draining rollout cannot be prepared.", 409);
            rollout.PreparationRequested = true;
            rollout.Status = TeamLabRolloutStatus.Preparing;
            rollout.LastError = null;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> SetAccessAsync(
        Guid rolloutId,
        bool open,
        CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, rollout =>
        {
            if (open && (rollout.Status != TeamLabRolloutStatus.Ready || rollout.DrainRequested))
                throw new TeamLabApiContractException("rollout_not_ready", "Prepare all rollout targets before opening access.", 409);
            rollout.DesiredAccessOpen = open;
            rollout.AccessOpenedAt = open ? DateTimeOffset.UtcNow : null;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestDrainAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, rollout =>
        {
            rollout.DesiredAccessOpen = false;
            rollout.DrainRequested = true;
            rollout.Status = TeamLabRolloutStatus.Draining;
            rollout.DrainingAt ??= DateTimeOffset.UtcNow;
        }, cancellationToken);

    public async Task<TeamLabRolloutTargetPageModel> ListTargetsAsync(
        Guid rolloutId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var rolloutKey = await context.TeamLabRollouts.AsNoTracking()
            .Where(item => item.PublicId == rolloutId)
            .Select(item => (int?)item.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("rollout_not_found", "The TeamLab rollout was not found.", 404);
        var take = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after);
        var query = context.TeamLabRolloutTargets.AsNoTracking().Where(item => item.RolloutId == rolloutKey);
        if (cursor is not null) query = query.Where(item => item.Id > cursor.Value);
        var rows = await query.OrderBy(item => item.Id).Take(take + 1)
            .Select(item => new
            {
                Target = item,
                RuntimePublicId = item.Runtime == null ? (Guid?)null : item.Runtime.PublicId,
                RuntimeStatus = item.Runtime == null ? (TeamLabRuntimeStatus?)null : item.Runtime.Status,
                RuntimeStage = item.Runtime == null ? null : item.Runtime.Status.ToString().ToLowerInvariant()
            }).ToArrayAsync(cancellationToken);
        var page = rows.Take(take).Select(item => new TeamLabRolloutTargetModel(
            item.Target.PublicId,
            item.Target.ExternalSubject,
            item.Target.DisplayName,
            item.RuntimePublicId,
            item.Target.Status.ToString().ToLowerInvariant(),
            item.Target.LastOperationId,
            item.RuntimeStatus,
            item.RuntimeStage,
            item.Target.CreatedAt,
            item.Target.UpdatedAt,
            item.Target.LastError)).ToArray();
        var next = rows.Length > take ? EncodeCursor(rows[take - 1].Target.Id) : null;
        return new TeamLabRolloutTargetPageModel(page, next);
    }

    private async Task<TeamLabRolloutModel> MutateAsync(
        Guid rolloutId,
        Action<TeamLabRollout> mutate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rollout = await context.TeamLabRollouts.Include(item => item.Targets)
                .SingleOrDefaultAsync(item => item.PublicId == rolloutId, cancellationToken)
                ?? throw new TeamLabApiContractException("rollout_not_found", "The TeamLab rollout was not found.", 404);
            mutate(rollout);
            rollout.Revision++;
            rollout.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return ToModel(rollout);
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                context.ChangeTracker.Clear();
            }
        }
        throw new TeamLabApiContractException(
            "rollout_concurrent_update", "The rollout changed concurrently. Retry the operation.", 409);
    }

    internal static TeamLabRolloutModel ToModel(TeamLabRollout rollout)
    {
        var targets = rollout.Targets;
        return new TeamLabRolloutModel(
            rollout.PublicId,
            rollout.ReleaseId,
            rollout.Status.ToString().ToLowerInvariant(),
            rollout.PreparationRequested,
            rollout.DesiredAccessOpen,
            rollout.DrainRequested,
            new TeamLabRolloutCountsModel(
                targets.Count,
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Pending),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Provisioning),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Ready),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.AccessOpen),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Failed),
                targets.Count(item => item.Status is TeamLabRolloutTargetStatus.Draining or TeamLabRolloutTargetStatus.CleanupPending),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Destroyed)),
            rollout.PreparedAt,
            rollout.AccessOpenedAt,
            rollout.DrainingAt,
            rollout.CompletedAt,
            rollout.CreatedAt,
            rollout.UpdatedAt,
            rollout.LastError);
    }

    private static int? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return int.TryParse(decoded, out var id) && id > 0 ? id : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TeamLabApiContractException("rollout_cursor_invalid", "The rollout target cursor is invalid.", 400);
        }
    }

    private static string EncodeCursor(int id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString()));
}
