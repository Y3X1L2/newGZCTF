using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application.Rollouts;

public sealed class TeamLabRolloutCoordinator(
    AppDbContext context,
    IEnumerable<ITeamLabRolloutTargetProvider> providers,
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabRuntimeOperationApplicationService operations,
    TeamLabAccessGrantService access,
    ImageDistributionService distribution,
    IDistributedLeaseProvider leases,
    ILogger<TeamLabRolloutCoordinator> logger)
{
    private const int TargetBatchSize = 8;

    public async Task<int> ProcessBatchAsync(int limit, CancellationToken cancellationToken)
    {
        var rollouts = await context.TeamLabRollouts.AsNoTracking()
            .Where(item => item.Status != TeamLabRolloutStatus.Completed && item.Status != TeamLabRolloutStatus.Archived &&
                           (item.PreparationRequested || item.DrainRequested ||
                            item.PauseRequested && item.Targets.Any(target => target.IsDesired &&
                                target.Status != TeamLabRolloutTargetStatus.Paused &&
                                target.Status != TeamLabRolloutTargetStatus.Destroyed &&
                                target.Status != TeamLabRolloutTargetStatus.Failed)))
            .OrderBy(item => item.UpdatedAt)
            .Select(item => new { item.Id, item.PublicId })
            .Take(Math.Clamp(limit, 1, 16))
            .ToArrayAsync(cancellationToken);
        foreach (var rollout in rollouts)
        {
            IDistributedLease lease;
            try
            {
                lease = await leases.AcquireAsync(
                    $"teamlab:rollout:{rollout.PublicId:D}",
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                logger.LogDebug("TeamLab rollout {RolloutId} 正在由其他 worker 协调", rollout.PublicId);
                continue;
            }
            await using (lease)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LeaseLost);
                try
                {
                    await ProcessOneAsync(rollout.Id, linked.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (lease.LeaseLost.IsCancellationRequested)
                {
                    logger.LogWarning("TeamLab rollout {RolloutId} 的 lease 已丢失；协调在提交下一个 target 前已停止", rollout.PublicId);
                }
                catch (DbUpdateConcurrencyException)
                {
                    logger.LogDebug("TeamLab rollout {RolloutId} 被并发修改；下一个 tick 将重新协调", rollout.PublicId);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "TeamLab rollout {RolloutId} 协调失败", rollout.PublicId);
                    await RecordFailureAsync(rollout.Id, exception.Message, cancellationToken);
                }
            }
        }
        return rollouts.Length;
    }

    private async Task ProcessOneAsync(int rolloutId, CancellationToken cancellationToken)
    {
        var rollout = await context.TeamLabRollouts
            .Include(item => item.Release)
            .Include(item => item.Targets)
            .ThenInclude(item => item.Runtime)
            .SingleAsync(item => item.Id == rolloutId, cancellationToken);
        var provider = providers.SingleOrDefault(item => item.AdapterKind == rollout.AdapterKind)
            ?? throw new InvalidOperationException($"没有 TeamLab rollout adapter 处理 '{rollout.AdapterKind}'");

        await provider.SynchronizeTargetsAsync(rollout, cancellationToken);
        await context.Entry(rollout).Collection(item => item.Targets).Query()
            .Include(item => item.Runtime).LoadAsync(cancellationToken);

        if (rollout.DrainRequested)
        {
            await DrainAsync(rollout, cancellationToken);
            return;
        }

        if (rollout.PauseRequested)
        {
            await ProcessPauseRequestsAsync(rollout, cancellationToken);
            await RefreshTargetFactsAsync(rollout, cancellationToken);
            return;
        }

        await ProcessRebuildRequestsAsync(rollout, cancellationToken);
        await RefreshTargetFactsAsync(rollout, cancellationToken);
        await ProcessResumeRequestsAsync(rollout, cancellationToken);

        if (!await PrepareImagesAsync(rollout, cancellationToken))
            return;

        if (rollout.Targets.Any(item =>
                item.IsDesired && !item.RebuildRequested && item.RuntimeId is null &&
                item.Status is TeamLabRolloutTargetStatus.Pending or TeamLabRolloutTargetStatus.Provisioning) &&
            (rollout.Status != TeamLabRolloutStatus.RollingOut || rollout.LastError is not null))
        {
            rollout.Status = TeamLabRolloutStatus.RollingOut;
            rollout.LastError = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        foreach (var target in rollout.Targets
                     .Where(item => item.IsDesired && !item.RebuildRequested && item.RuntimeId is null &&
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
        var active = rollout.Targets.Where(item => item.IsDesired && item.Status != TeamLabRolloutTargetStatus.Destroyed).ToArray();
        var now = DateTimeOffset.UtcNow;
        string? nextError = null;
        TeamLabRolloutStatus nextStatus;
        if (active.Length == 0)
        {
            nextStatus = TeamLabRolloutStatus.Blocked;
            nextError = "rollout 没有期望的 targets；打开访问前请先替换 target 快照";
        }
        else if (active.Any(item => item.Status == TeamLabRolloutTargetStatus.Failed))
        {
            nextStatus = TeamLabRolloutStatus.Blocked;
            nextError = "一个或多个 rollout targets 失败；请先检查 target 错误信息再重建或清理";
        }
        else if (active.All(item => item.Status is TeamLabRolloutTargetStatus.Ready or TeamLabRolloutTargetStatus.AccessOpen))
        {
            nextStatus = TeamLabRolloutStatus.Ready;
        }
        else
        {
            nextStatus = TeamLabRolloutStatus.RollingOut;
        }
        var preparedAtChanged = nextStatus == TeamLabRolloutStatus.Ready && rollout.PreparedAt is null;
        var preparationCompleted = nextStatus is TeamLabRolloutStatus.Ready or TeamLabRolloutStatus.Blocked;
        if (preparedAtChanged)
            rollout.PreparedAt = now;
        if (preparationCompleted)
            rollout.PreparationRequested = false;
        if (nextStatus != rollout.Status || nextError != rollout.LastError || preparedAtChanged || preparationCompleted)
        {
            rollout.Status = nextStatus;
            rollout.LastError = nextError;
            rollout.Revision++;
            rollout.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ProcessRebuildRequestsAsync(
        TeamLabRollout rollout,
        CancellationToken cancellationToken)
    {
        foreach (var target in rollout.Targets.Where(item =>
                     item.IsDesired && item.RebuildRequested &&
                     item.Status == TeamLabRolloutTargetStatus.Failed).OrderBy(item => item.Id))
        {
            if (target.RuntimeId is not { } runtimeId)
            {
                target.Status = TeamLabRolloutTargetStatus.Pending;
                target.RebuildRequested = false;
                target.LastError = null;
                target.UpdatedAt = DateTimeOffset.UtcNow;
                continue;
            }

            var runtime = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == runtimeId, cancellationToken);
            if (runtime is null || runtime.Status == TeamLabRuntimeStatus.Destroyed)
            {
                target.RuntimeId = null;
                target.Status = TeamLabRolloutTargetStatus.Pending;
                target.RebuildRequested = false;
                target.LastError = null;
                target.UpdatedAt = DateTimeOffset.UtcNow;
                continue;
            }

            await runtimes.DestroyRolloutTargetAndEnqueueAsync(
                runtime.PublicId,
                rollout.Id,
                target.LastOperationId,
                rollout.CreatedByUserId,
                cancellationToken);
            target.Status = TeamLabRolloutTargetStatus.Draining;
            target.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessPauseRequestsAsync(
        TeamLabRollout rollout,
        CancellationToken cancellationToken)
    {
        foreach (var target in rollout.Targets
                     .Where(item => item.IsDesired && !item.RebuildRequested && item.RuntimeId is { } runtimeId &&
                                    item.Status is TeamLabRolloutTargetStatus.Ready or
                                        TeamLabRolloutTargetStatus.AccessOpen)
                     .OrderBy(item => item.Id)
                     .Take(TargetBatchSize))
        {
            var runtime = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == target.RuntimeId, cancellationToken);
            if (runtime is null || runtime.Status == TeamLabRuntimeStatus.Paused)
                continue;
            // A deployment is paused only after it reaches a stable running state. Submitting
            // a pause command while its startup operation is still active leaves both state
            // machines waiting on an operation the runtime cannot execute.
            if (runtime.Status != TeamLabRuntimeStatus.Running)
                continue;
            await operations.SubmitRolloutTargetLifecycleAsync(
                null,
                rollout.CreatedByUserId,
                $"teamlab-target-{target.PublicId:N}-pause-r{rollout.Revision}",
                runtime.PublicId,
                rollout.PublicId,
                target.PublicId,
                rollout.ControlScopeId,
                pause: true,
                cancellationToken);
        }
    }

    private async Task ProcessResumeRequestsAsync(
        TeamLabRollout rollout,
        CancellationToken cancellationToken)
    {
        foreach (var target in rollout.Targets
                     .Where(item => item.IsDesired && item.RuntimeId is { } runtimeId &&
                                    item.Status == TeamLabRolloutTargetStatus.Paused)
                     .OrderBy(item => item.Id)
                     .Take(TargetBatchSize))
        {
            var runtime = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == target.RuntimeId, cancellationToken);
            if (runtime is null || runtime.Status != TeamLabRuntimeStatus.Paused)
                continue;
            await operations.SubmitRolloutTargetLifecycleAsync(
                null,
                rollout.CreatedByUserId,
                $"teamlab-target-{target.PublicId:N}-resume-r{rollout.Revision}",
                runtime.PublicId,
                rollout.PublicId,
                target.PublicId,
                rollout.ControlScopeId,
                pause: false,
                cancellationToken);
        }
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
            rollout.PreparationRequested = false;
            rollout.LastError = missingTemplate == 0
                ? "没有可调度的 node 能承载一个或多个 release images"
                : $"没有可调度的 node 能承载 image template {missingTemplate}";
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
        var failed = records.FirstOrDefault(item => item.Status == ImageDistributionStatus.Failed);
        if (failed is not null)
        {
            rollout.Status = TeamLabRolloutStatus.Blocked;
            rollout.PreparationRequested = false;
            rollout.LastError = Limit(failed.ErrorMessage ?? "image 分发失败");
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

    private async Task<bool> RefreshTargetFactsAsync(TeamLabRollout rollout, CancellationToken cancellationToken)
    {
        var runtimeIds = rollout.Targets.Where(item => item.RuntimeId.HasValue)
            .Select(item => item.RuntimeId!.Value).ToArray();
        var facts = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => runtimeIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Status, item.LastError })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var changed = false;
        var now = DateTimeOffset.UtcNow;
        foreach (var target in rollout.Targets.Where(item => item.RuntimeId.HasValue))
        {
            if (!facts.TryGetValue(target.RuntimeId!.Value, out var fact)) continue;
            if (target.Status == TeamLabRolloutTargetStatus.Draining)
            {
                if (fact.Status == TeamLabRuntimeStatus.Destroyed)
                {
                    if (target.RebuildRequested)
                    {
                        target.RuntimeId = null;
                        target.Status = TeamLabRolloutTargetStatus.Pending;
                        target.RebuildRequested = false;
                        target.LastError = null;
                        target.ReadyAt = null;
                        target.DestroyedAt = null;
                    }
                    else
                    {
                        target.Status = TeamLabRolloutTargetStatus.Destroyed;
                        target.DestroyedAt ??= now;
                    }
                    target.UpdatedAt = now;
                    changed = true;
                }
                else
                {
                    var teardownStatus = await context.DeploymentQueueTickets.AsNoTracking()
                        .Where(item => item.TeamLabRuntimeId == target.RuntimeId &&
                                       item.Operation == RuntimeOperationKind.Destroy)
                        .OrderByDescending(item => item.CreatedAt)
                        .ThenByDescending(item => item.Id)
                        .Select(item => (DeploymentQueueTicketStatus?)item.Status)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (teardownStatus is DeploymentQueueTicketStatus.Pending or
                        DeploymentQueueTicketStatus.Scheduling or DeploymentQueueTicketStatus.Scheduled or
                        DeploymentQueueTicketStatus.Running or DeploymentQueueTicketStatus.Succeeded)
                        continue;
                    target.Status = TeamLabRolloutTargetStatus.Failed;
                    target.LastError = target.RebuildRequested
                        ? "target 清理未完成；请重试 rebuild 或 drain 命令"
                        : "target 清理未完成；drain 将持续重试，直到 workload 被销毁";
                    if (target.RebuildRequested)
                        target.RebuildRequested = false;
                    target.UpdatedAt = now;
                    changed = true;
                }
                continue;
            }
            if (target.Status == TeamLabRolloutTargetStatus.Failed && fact.Status != TeamLabRuntimeStatus.Failed &&
                fact.Status is not (TeamLabRuntimeStatus.Destroying or TeamLabRuntimeStatus.CleanupPending or TeamLabRuntimeStatus.Destroyed))
                continue;
            var nextStatus = fact.Status switch
            {
                TeamLabRuntimeStatus.Running => rollout.DesiredAccessOpen
                    ? TeamLabRolloutTargetStatus.AccessOpen
                    : TeamLabRolloutTargetStatus.Ready,
                TeamLabRuntimeStatus.Paused => TeamLabRolloutTargetStatus.Paused,
                TeamLabRuntimeStatus.Failed => TeamLabRolloutTargetStatus.Failed,
                TeamLabRuntimeStatus.Destroying => TeamLabRolloutTargetStatus.Draining,
                TeamLabRuntimeStatus.CleanupPending => TeamLabRolloutTargetStatus.CleanupPending,
                TeamLabRuntimeStatus.Destroyed => TeamLabRolloutTargetStatus.Destroyed,
                _ => TeamLabRolloutTargetStatus.Provisioning
            };
            var nextError = fact.Status == TeamLabRuntimeStatus.Failed ? fact.LastError : null;
            var nextReadyAt = target.ReadyAt ?? (fact.Status == TeamLabRuntimeStatus.Running ? now : null);
            var nextDestroyedAt = target.DestroyedAt ?? (fact.Status == TeamLabRuntimeStatus.Destroyed ? now : null);
            if (target.Status == nextStatus && target.LastError == nextError &&
                target.ReadyAt == nextReadyAt && target.DestroyedAt == nextDestroyedAt)
                continue;
            target.Status = nextStatus;
            target.LastError = nextError;
            target.ReadyAt = nextReadyAt;
            target.DestroyedAt = nextDestroyedAt;
            target.UpdatedAt = now;
            changed = true;
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);

        if (!rollout.DesiredAccessOpen)
        {
            var openRuntimes = await context.TeamLabRuntimes.AsNoTracking()
                .Where(item => runtimeIds.Contains(item.Id) && item.IsOpenToPlayers)
                .Select(item => item.PublicId)
                .ToArrayAsync(cancellationToken);
            foreach (var runtimeId in openRuntimes)
                await access.RevokeAllAsync(runtimeId, cancellationToken);
        }
        return changed;
    }

    private async Task DrainAsync(TeamLabRollout rollout, CancellationToken cancellationToken)
    {
        var changed = rollout.Status != TeamLabRolloutStatus.Draining;
        rollout.Status = TeamLabRolloutStatus.Draining;
        var now = DateTimeOffset.UtcNow;
        foreach (var target in rollout.Targets.Where(item =>
                     !item.RuntimeId.HasValue && item.Status != TeamLabRolloutTargetStatus.Destroyed))
        {
            target.Status = TeamLabRolloutTargetStatus.Destroyed;
            target.DestroyedAt ??= now;
            target.UpdatedAt = now;
            changed = true;
        }
        foreach (var target in rollout.Targets
                     .Where(item => item.RuntimeId.HasValue && item.Status != TeamLabRolloutTargetStatus.Destroyed &&
                                    item.Status != TeamLabRolloutTargetStatus.Draining)
                     .OrderBy(item => item.Id)
                     .Take(TargetBatchSize))
        {
            var runtime = await context.TeamLabRuntimes.AsNoTracking()
                .SingleAsync(item => item.Id == target.RuntimeId, cancellationToken);
            await runtimes.DestroyRolloutTargetAndEnqueueAsync(runtime.PublicId, rollout.Id, target.LastOperationId, rollout.CreatedByUserId,
                cancellationToken);
            target.Status = TeamLabRolloutTargetStatus.Draining;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;
        }
        changed |= await RefreshTargetFactsAsync(rollout, cancellationToken);
        if (rollout.Targets.All(item => item.Status == TeamLabRolloutTargetStatus.Destroyed))
        {
            changed |= rollout.Status != TeamLabRolloutStatus.Completed || rollout.CompletedAt is null;
            rollout.Status = TeamLabRolloutStatus.Completed;
            rollout.CompletedAt = DateTimeOffset.UtcNow;
            await distribution.ReleaseTeamLabRolloutReferencesAsync(rollout.Id, cancellationToken);
        }
        if (changed)
        {
            rollout.Revision++;
            rollout.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
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
