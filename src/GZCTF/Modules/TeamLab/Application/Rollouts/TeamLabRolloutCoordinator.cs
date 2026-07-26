using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application.Rollouts;

public sealed class TeamLabRolloutCoordinator(
    AppDbContext context,
    IEnumerable<ITeamLabRolloutTargetProvider> providers,
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabAccessGrantService access,
    ImageDistributionService distribution,
    ILogger<TeamLabRolloutCoordinator> logger)
{
    private const int TargetBatchSize = 8;

    public async Task<int> ProcessBatchAsync(int limit, CancellationToken cancellationToken)
    {
        var ids = await context.TeamLabRollouts.AsNoTracking()
            .Where(item => item.PreparationRequested && item.Status != TeamLabRolloutStatus.Completed ||
                           item.DrainRequested && item.Status != TeamLabRolloutStatus.Completed)
            .OrderBy(item => item.UpdatedAt)
            .Select(item => item.Id)
            .Take(Math.Clamp(limit, 1, 16))
            .ToArrayAsync(cancellationToken);
        foreach (var id in ids)
        {
            try
            {
                await ProcessOneAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogDebug("TeamLab rollout {RolloutId} changed concurrently; the next tick will reconcile it.", id);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "TeamLab rollout {RolloutId} reconciliation failed.", id);
                await RecordFailureAsync(id, exception.Message, cancellationToken);
            }
        }
        return ids.Length;
    }

    private async Task ProcessOneAsync(int rolloutId, CancellationToken cancellationToken)
    {
        var rollout = await context.TeamLabRollouts
            .Include(item => item.Release)
            .Include(item => item.Targets)
            .ThenInclude(item => item.Runtime)
            .SingleAsync(item => item.Id == rolloutId, cancellationToken);
        var provider = providers.SingleOrDefault(item => item.AdapterKind == rollout.AdapterKind)
            ?? throw new InvalidOperationException($"No TeamLab rollout adapter handles '{rollout.AdapterKind}'.");

        await provider.SynchronizeTargetsAsync(rollout, cancellationToken);
        await context.Entry(rollout).Collection(item => item.Targets).Query()
            .Include(item => item.Runtime).LoadAsync(cancellationToken);

        if (rollout.DrainRequested)
        {
            await DrainAsync(rollout, cancellationToken);
            return;
        }

        if (!await PrepareImagesAsync(rollout, cancellationToken))
            return;

        rollout.Status = TeamLabRolloutStatus.RollingOut;
        rollout.LastError = null;
        await context.SaveChangesAsync(cancellationToken);

        foreach (var target in rollout.Targets
                     .Where(item => item.RuntimeId is null &&
                                    item.Status is TeamLabRolloutTargetStatus.Pending or
                                        TeamLabRolloutTargetStatus.Provisioning)
                     .OrderBy(item => item.Id)
                     .Take(TargetBatchSize))
        {
            target.Status = TeamLabRolloutTargetStatus.Provisioning;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            try
            {
                var provisioned = await provider.ProvisionAsync(rollout, target, cancellationToken);
                target.RuntimeId = provisioned.RuntimeId;
                target.LastOperationId = provisioned.OperationId;
                target.LastError = null;
            }
            catch (Exception exception)
            {
                target.Status = TeamLabRolloutTargetStatus.Failed;
                target.LastError = Limit(exception.Message);
            }
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }

        await RefreshTargetFactsAsync(rollout, cancellationToken);
        var active = rollout.Targets.Where(item => item.Status != TeamLabRolloutTargetStatus.Destroyed).ToArray();
        if (active.Any(item => item.Status == TeamLabRolloutTargetStatus.Failed))
        {
            rollout.Status = TeamLabRolloutStatus.Blocked;
            rollout.LastError = "One or more rollout targets failed. Inspect the target error before rebuilding or cleaning it.";
        }
        else if (active.All(item => item.Status is TeamLabRolloutTargetStatus.Ready or TeamLabRolloutTargetStatus.AccessOpen))
        {
            rollout.Status = TeamLabRolloutStatus.Ready;
            rollout.PreparedAt ??= DateTimeOffset.UtcNow;
        }
        rollout.Revision++;
        rollout.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> PrepareImagesAsync(TeamLabRollout rollout, CancellationToken cancellationToken)
    {
        var definition = TeamLabReleaseCodec.DecodeExecution(rollout.Release.SchemaVersion, rollout.Release.CanonicalJson);
        var templateIds = definition.Assets.Select(item => item.ImageTemplateId).Distinct().Order().ToArray();
        foreach (var templateId in templateIds)
            await distribution.DistributeTemplateAsync(
                templateId,
                ImageDistributionReferenceKey.TeamLabRollout(rollout.Id),
                cancellationToken);
        if (templateIds.Length == 0) return true;

        var records = await context.ImageDistributionRecords.AsNoTracking()
            .Where(item => templateIds.Contains(item.ImageTemplateId) &&
                           item.References.Any(reference =>
                               reference.Kind == ImageDistributionReferenceKind.TeamLabRollout &&
                               reference.ResourceId == rollout.Id))
            .Select(item => new { item.ImageTemplateId, item.Status, item.ErrorMessage })
            .ToArrayAsync(cancellationToken);
        var missingTemplate = templateIds.FirstOrDefault(templateId =>
            records.All(record => record.ImageTemplateId != templateId));
        if (records.Length == 0 || missingTemplate != 0)
        {
            rollout.Status = TeamLabRolloutStatus.Blocked;
            rollout.LastError = missingTemplate == 0
                ? "No schedulable node can host one or more release images."
                : $"No schedulable node can host image template {missingTemplate}.";
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
        var failed = records.FirstOrDefault(item => item.Status == ImageDistributionStatus.Failed);
        if (failed is not null)
        {
            rollout.Status = TeamLabRolloutStatus.Blocked;
            rollout.LastError = Limit(failed.ErrorMessage ?? "Image distribution failed.");
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
        if (records.Any(item => item.Status != ImageDistributionStatus.Ready))
        {
            rollout.Status = TeamLabRolloutStatus.Preparing;
            rollout.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
        return true;
    }

    private async Task RefreshTargetFactsAsync(TeamLabRollout rollout, CancellationToken cancellationToken)
    {
        var runtimeIds = rollout.Targets.Where(item => item.RuntimeId.HasValue)
            .Select(item => item.RuntimeId!.Value).ToArray();
        var facts = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => runtimeIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Status, item.LastError })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var target in rollout.Targets.Where(item => item.RuntimeId.HasValue))
        {
            if (!facts.TryGetValue(target.RuntimeId!.Value, out var fact)) continue;
            target.Status = fact.Status switch
            {
                TeamLabRuntimeStatus.Running => rollout.DesiredAccessOpen
                    ? TeamLabRolloutTargetStatus.AccessOpen
                    : TeamLabRolloutTargetStatus.Ready,
                TeamLabRuntimeStatus.Failed => TeamLabRolloutTargetStatus.Failed,
                TeamLabRuntimeStatus.Destroying => TeamLabRolloutTargetStatus.Draining,
                TeamLabRuntimeStatus.CleanupPending => TeamLabRolloutTargetStatus.CleanupPending,
                TeamLabRuntimeStatus.Destroyed => TeamLabRolloutTargetStatus.Destroyed,
                _ => TeamLabRolloutTargetStatus.Provisioning
            };
            target.LastError = fact.Status == TeamLabRuntimeStatus.Failed ? fact.LastError : null;
            target.ReadyAt ??= fact.Status == TeamLabRuntimeStatus.Running ? DateTimeOffset.UtcNow : null;
            target.DestroyedAt ??= fact.Status == TeamLabRuntimeStatus.Destroyed ? DateTimeOffset.UtcNow : null;
            target.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (!rollout.DesiredAccessOpen)
        {
            var openRuntimes = await context.TeamLabRuntimes.AsNoTracking()
                .Where(item => runtimeIds.Contains(item.Id) && item.IsOpenToPlayers)
                .Select(item => item.PublicId)
                .ToArrayAsync(cancellationToken);
            foreach (var runtimeId in openRuntimes)
                await access.RevokeAllAsync(runtimeId, cancellationToken);
        }
    }

    private async Task DrainAsync(TeamLabRollout rollout, CancellationToken cancellationToken)
    {
        rollout.Status = TeamLabRolloutStatus.Draining;
        await RefreshTargetFactsAsync(rollout, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var target in rollout.Targets.Where(item =>
                     !item.RuntimeId.HasValue && item.Status != TeamLabRolloutTargetStatus.Destroyed))
        {
            target.Status = TeamLabRolloutTargetStatus.Destroyed;
            target.DestroyedAt ??= now;
            target.UpdatedAt = now;
        }
        foreach (var target in rollout.Targets
                     .Where(item => item.RuntimeId.HasValue && item.Status != TeamLabRolloutTargetStatus.Destroyed &&
                                    item.Status is not TeamLabRolloutTargetStatus.Draining and
                                        not TeamLabRolloutTargetStatus.CleanupPending)
                     .OrderBy(item => item.Id)
                     .Take(TargetBatchSize))
        {
            var runtime = await context.TeamLabRuntimes.AsNoTracking()
                .SingleAsync(item => item.Id == target.RuntimeId, cancellationToken);
            var operationId = target.LastOperationId ?? Guid.CreateVersion7();
            await runtimes.DestroyAndEnqueueAsync(runtime.PublicId, operationId, rollout.CreatedByUserId,
                cancellationToken);
            target.LastOperationId = operationId;
            target.Status = TeamLabRolloutTargetStatus.Draining;
            target.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await context.SaveChangesAsync(cancellationToken);
        await RefreshTargetFactsAsync(rollout, cancellationToken);
        if (rollout.Targets.All(item => item.Status == TeamLabRolloutTargetStatus.Destroyed))
        {
            rollout.Status = TeamLabRolloutStatus.Completed;
            rollout.CompletedAt = DateTimeOffset.UtcNow;
            await distribution.ReleaseTeamLabRolloutReferencesAsync(rollout.Id, cancellationToken);
        }
        rollout.Revision++;
        rollout.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(int rolloutId, string message, CancellationToken cancellationToken)
    {
        await context.TeamLabRollouts.Where(item => item.Id == rolloutId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, TeamLabRolloutStatus.Blocked)
                .SetProperty(item => item.LastError, Limit(message))
                .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    private static string Limit(string message) => message.Length <= 2048 ? message : message[..2048];
}
