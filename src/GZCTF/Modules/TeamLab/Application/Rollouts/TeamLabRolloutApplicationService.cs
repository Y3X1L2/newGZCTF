using System.Text;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application.Rollouts;

public sealed class TeamLabRolloutApplicationService(
    AppDbContext context,
    TeamLabControlScopeService? controlScopes = null) : ITeamLabRolloutApplicationService
{
    private TeamLabControlScopeService Scopes => controlScopes ?? new TeamLabControlScopeService(context);

    public async Task<TeamLabRolloutModel> CreateExternalAsync(
        CreateTeamLabRolloutModel command,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var scope = await Scopes.RequireWritableAsync(command.ControlScopeId, cancellationToken);
        var release = await context.TeamLabTopologyReleases
            .Include(item => item.Topology)
            .SingleOrDefaultAsync(item => item.Id == command.ReleaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑 release", 404);
        if (release.ControlScopeId != scope.Id)
            throw new TeamLabApiContractException("release_not_found", "此 control scope 中未找到拓扑 release", 404);

        var externalReference = NormalizeExternalReference(command.ExternalReference);
        var targets = NormalizeTargets(command.Targets);
        var existing = await context.TeamLabRollouts
            .Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.ControlScopeId == scope.Id &&
                                          item.ReleaseId == release.Id &&
                                          item.AdapterKind == "external" &&
                                          item.ExternalReference == externalReference &&
                                          item.Status != TeamLabRolloutStatus.Archived,
                cancellationToken);
        if (existing is not null)
            throw new TeamLabApiContractException("rollout_reference_conflict", "rollout 的 external reference 已被使用", 409);

        var rollout = new TeamLabRollout
        {
            ControlScopeId = scope.Id,
            ReleaseId = release.Id,
            OwnerUserId = release.Topology.OwnerUserId ?? actorUserId,
            CreatedByUserId = actorUserId,
            AdapterKind = "external",
            ExternalReference = externalReference,
            CreatedByOperationId = operationId,
            LastMutationOperationId = operationId
        };
        foreach (var target in targets)
            rollout.Targets.Add(new TeamLabRolloutTarget
            {
                ExternalSubject = target.ExternalSubject,
                DisplayName = target.DisplayName
            });
        context.TeamLabRollouts.Add(rollout);
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(rollout);
    }

    public async Task<TeamLabRolloutModel> ReplaceTargetsAsync(
        Guid rolloutId,
        ReplaceTeamLabRolloutTargetsModel command,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rollout = await LoadExternalAsync(rolloutId, cancellationToken);
        await Scopes.RequireWritableAsync(rollout.ControlScopeId ?? Guid.Empty, cancellationToken);
        if (rollout.DrainRequested || rollout.Status is TeamLabRolloutStatus.Draining or TeamLabRolloutStatus.Completed or TeamLabRolloutStatus.Archived)
            throw new TeamLabApiContractException("rollout_not_mutable", "清理中或已归档的 rollout 无法更改其 targets", 409);
        var targets = NormalizeTargets(command.Targets);
        var desired = targets.ToDictionary(item => item.ExternalSubject, StringComparer.Ordinal);
        foreach (var existing in rollout.Targets)
        {
            existing.IsDesired = desired.ContainsKey(existing.ExternalSubject);
            if (desired.TryGetValue(existing.ExternalSubject, out var replacement))
                existing.DisplayName = replacement.DisplayName;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        foreach (var target in targets.Where(item => rollout.Targets.All(existing => existing.ExternalSubject != item.ExternalSubject)))
            rollout.Targets.Add(new TeamLabRolloutTarget
            {
                ExternalSubject = target.ExternalSubject,
                DisplayName = target.DisplayName
            });
        rollout.LastMutationOperationId = operationId;
        rollout.PreparationRequested = true;
        rollout.Status = TeamLabRolloutStatus.Preparing;
        rollout.Revision++;
        rollout.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(rollout);
    }

    public async Task<TeamLabRolloutModel> RequestRebuildAsync(
        Guid rolloutId,
        Guid targetId,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rollout = await LoadExternalAsync(rolloutId, cancellationToken);
        await Scopes.RequireWritableAsync(rollout.ControlScopeId ?? Guid.Empty, cancellationToken);
        var target = rollout.Targets.SingleOrDefault(item => item.PublicId == targetId)
            ?? throw new TeamLabApiContractException("rollout_target_not_found", "未找到 rollout target", 404);
        if (!target.IsDesired)
            throw new TeamLabApiContractException("rollout_target_removed", "该 target 已不属于期望的 rollout", 409);
        if (target.Status != TeamLabRolloutTargetStatus.Failed)
            throw new TeamLabApiContractException("rollout_target_not_failed", "只有失败的 target 才能重建", 409);
        target.RebuildRequested = true;
        target.LastOperationId = operationId;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        rollout.PreparationRequested = true;
        rollout.Status = TeamLabRolloutStatus.Preparing;
        rollout.LastError = null;
        rollout.LastMutationOperationId = operationId;
        rollout.Revision++;
        rollout.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(rollout);
    }

    public async Task<TeamLabRolloutModel> ArchiveAsync(
        Guid rolloutId,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rollout = await LoadExternalAsync(rolloutId, cancellationToken);
        await Scopes.RequireWritableAsync(rollout.ControlScopeId ?? Guid.Empty, cancellationToken);
        if (rollout.Targets.Any(item => item.Status != TeamLabRolloutTargetStatus.Destroyed))
            throw new TeamLabApiContractException("rollout_not_drained", "归档前请先清理所有 rollout targets", 409);
        rollout.Status = TeamLabRolloutStatus.Archived;
        rollout.DesiredAccessOpen = false;
        rollout.DrainRequested = true;
        rollout.CompletedAt ??= DateTimeOffset.UtcNow;
        rollout.LastMutationOperationId = operationId;
        rollout.Revision++;
        rollout.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(rollout);
    }

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
            var scopeId = await context.TeamLabTopologyReleases.AsNoTracking()
                .Where(item => item.Id == releaseId)
                .Select(item => item.ControlScopeId)
                .SingleOrDefaultAsync(cancellationToken);
            scopeId ??= (await Scopes.EnsurePlatformScopeAsync(cancellationToken)).Id;
            rollout = new TeamLabRollout
            {
                ReleaseId = releaseId,
                ControlScopeId = scopeId,
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

    public async Task<TeamLabRolloutModel?> GetByStorageIdAsync(int rolloutId, CancellationToken cancellationToken)
    {
        var rollout = await context.TeamLabRollouts.AsNoTracking()
            .Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.Id == rolloutId, cancellationToken);
        return rollout is null ? null : ToModel(rollout);
    }

    public Task<int> GetStorageIdAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        context.TeamLabRollouts.AsNoTracking()
            .Where(item => item.PublicId == rolloutId)
            .Select(item => item.Id)
            .SingleAsync(cancellationToken);

    public async Task<TeamLabRolloutPageModel> ListExternalAsync(
        Guid controlScopeId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var cursor = DecodeCursor(after);
        var take = Math.Clamp(limit, 1, 100);
        var query = context.TeamLabRollouts
            .Include(item => item.Targets)
            .AsNoTracking()
            .Where(item => item.ControlScopeId == controlScopeId && item.AdapterKind == "external" &&
                           item.Status != TeamLabRolloutStatus.Archived);
        if (cursor is not null) query = query.Where(item => item.Id > cursor.Value);
        var rows = await query.OrderBy(item => item.Id).Take(take + 1).ToArrayAsync(cancellationToken);
        var items = rows.Take(take).Select(ToModel).ToArray();
        var next = rows.Length > take ? EncodeCursor(rows[take - 1].Id) : null;
        return new TeamLabRolloutPageModel(items, next);
    }

    public Task<TeamLabRolloutModel> RequestPreparationAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, null, rollout =>
        {
            if (rollout.DrainRequested)
                throw new TeamLabApiContractException("rollout_draining", "清理中的 rollout 无法准备", 409);
            if (rollout.PauseRequested)
                throw new TeamLabApiContractException("rollout_paused", "请求准备前请先恢复 rollout", 409);
            rollout.PreparationRequested = true;
            rollout.Status = TeamLabRolloutStatus.Preparing;
            rollout.LastError = null;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestPreparationForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, operationId, rollout =>
        {
            if (rollout.DrainRequested)
                throw new TeamLabApiContractException("rollout_draining", "清理中的 rollout 无法准备", 409);
            if (rollout.PauseRequested)
                throw new TeamLabApiContractException("rollout_paused", "请求准备前请先恢复 rollout", 409);
            rollout.PreparationRequested = true;
            rollout.Status = TeamLabRolloutStatus.Preparing;
            rollout.LastError = null;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> SetAccessAsync(
        Guid rolloutId,
        bool open,
        CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, null, rollout =>
        {
            if (open && (rollout.Status != TeamLabRolloutStatus.Ready || rollout.DrainRequested))
                throw new TeamLabApiContractException("rollout_not_ready", "打开访问前请先准备所有 rollout targets", 409);
            if (open && rollout.PauseRequested)
                throw new TeamLabApiContractException("rollout_paused", "打开访问前请先恢复 rollout", 409);
            rollout.DesiredAccessOpen = open;
            rollout.AccessOpenedAt = open ? DateTimeOffset.UtcNow : null;
            rollout.PreparationRequested = true;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> SetAccessForOperationAsync(Guid rolloutId, bool open, Guid operationId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, operationId, rollout =>
        {
            if (open && (rollout.Status != TeamLabRolloutStatus.Ready || rollout.DrainRequested))
                throw new TeamLabApiContractException("rollout_not_ready", "打开访问前请先准备所有 rollout targets", 409);
            if (open && rollout.PauseRequested)
                throw new TeamLabApiContractException("rollout_paused", "打开访问前请先恢复 rollout", 409);
            rollout.DesiredAccessOpen = open;
            rollout.AccessOpenedAt = open ? DateTimeOffset.UtcNow : null;
            rollout.PreparationRequested = true;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestDrainAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, null, rollout =>
        {
            rollout.DesiredAccessOpen = false;
            rollout.DrainRequested = true;
            rollout.PauseRequested = false;
            rollout.PreparationRequested = true;
            rollout.Status = TeamLabRolloutStatus.Draining;
            rollout.DrainingAt ??= DateTimeOffset.UtcNow;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestDrainForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, operationId, rollout =>
        {
            rollout.DesiredAccessOpen = false;
            rollout.DrainRequested = true;
            rollout.PauseRequested = false;
            rollout.PreparationRequested = true;
            rollout.Status = TeamLabRolloutStatus.Draining;
            rollout.DrainingAt ??= DateTimeOffset.UtcNow;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestPauseAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, null, rollout =>
        {
            if (rollout.DrainRequested || rollout.Status is TeamLabRolloutStatus.Draining or TeamLabRolloutStatus.Completed or TeamLabRolloutStatus.Archived)
                throw new TeamLabApiContractException("rollout_not_pausable", "清理中、已完成或已归档的 rollout 无法暂停", 409);
            rollout.PauseRequested = true;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestPauseForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, operationId, rollout =>
        {
            if (rollout.DrainRequested || rollout.Status is TeamLabRolloutStatus.Draining or TeamLabRolloutStatus.Completed or TeamLabRolloutStatus.Archived)
                throw new TeamLabApiContractException("rollout_not_pausable", "清理中、已完成或已归档的 rollout 无法暂停", 409);
            rollout.PauseRequested = true;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestResumeAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, null, rollout =>
        {
            if (rollout.DrainRequested || rollout.Status is TeamLabRolloutStatus.Draining or TeamLabRolloutStatus.Completed or TeamLabRolloutStatus.Archived)
                throw new TeamLabApiContractException("rollout_not_resumable", "清理中、已完成或已归档的 rollout 无法恢复", 409);
            rollout.PauseRequested = false;
            rollout.PreparationRequested = true;
        }, cancellationToken);

    public Task<TeamLabRolloutModel> RequestResumeForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken) =>
        MutateAsync(rolloutId, operationId, rollout =>
        {
            if (rollout.DrainRequested || rollout.Status is TeamLabRolloutStatus.Draining or TeamLabRolloutStatus.Completed or TeamLabRolloutStatus.Archived)
                throw new TeamLabApiContractException("rollout_not_resumable", "清理中、已完成或已归档的 rollout 无法恢复", 409);
            rollout.PauseRequested = false;
            rollout.PreparationRequested = true;
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
            ?? throw new TeamLabApiContractException("rollout_not_found", "未找到 TeamLab rollout", 404);
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

    public async Task<TeamLabRolloutTargetModel?> GetTargetAsync(
        Guid rolloutId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var row = await (
            from target in context.TeamLabRolloutTargets.AsNoTracking()
            join rollout in context.TeamLabRollouts.AsNoTracking()
                on target.RolloutId equals rollout.Id
            where rollout.PublicId == rolloutId && target.PublicId == targetId
            select new
            {
                Target = target,
                RuntimePublicId = target.Runtime == null ? (Guid?)null : target.Runtime.PublicId,
                RuntimeStatus = target.Runtime == null ? (TeamLabRuntimeStatus?)null : target.Runtime.Status
            }).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;
        return new TeamLabRolloutTargetModel(
            row.Target.PublicId,
            row.Target.ExternalSubject,
            row.Target.DisplayName,
            row.RuntimePublicId,
            row.Target.Status.ToString().ToLowerInvariant(),
            row.Target.LastOperationId,
            row.RuntimeStatus,
            row.RuntimeStatus?.ToString().ToLowerInvariant(),
            row.Target.CreatedAt,
            row.Target.UpdatedAt,
            row.Target.LastError);
    }

    private async Task<TeamLabRolloutModel> MutateAsync(
        Guid rolloutId,
        Guid? operationId,
        Action<TeamLabRollout> mutate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rollout = await context.TeamLabRollouts.Include(item => item.Targets)
                .SingleOrDefaultAsync(item => item.PublicId == rolloutId, cancellationToken)
                ?? throw new TeamLabApiContractException("rollout_not_found", "未找到 TeamLab rollout", 404);
            mutate(rollout);
            if (operationId is { } mutationId)
                rollout.LastMutationOperationId = mutationId;
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
            "rollout_concurrent_update", "rollout 已被并发修改，请重试该操作", 409);
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
            rollout.PauseRequested,
            new TeamLabRolloutCountsModel(
                targets.Count,
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Pending),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Provisioning),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Ready),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.AccessOpen),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Failed),
                targets.Count(item => item.Status is TeamLabRolloutTargetStatus.Draining or TeamLabRolloutTargetStatus.CleanupPending),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Destroyed),
                targets.Count(item => item.Status == TeamLabRolloutTargetStatus.Paused)),
            rollout.PreparedAt,
            rollout.AccessOpenedAt,
            rollout.DrainingAt,
            rollout.CompletedAt,
            rollout.CreatedAt,
            rollout.UpdatedAt,
            rollout.LastError,
            rollout.ControlScopeId,
            rollout.AdapterKind,
            rollout.ExternalReference,
            rollout.Revision);
    }

    private async Task<TeamLabRollout> LoadExternalAsync(Guid rolloutId, CancellationToken cancellationToken) =>
        await context.TeamLabRollouts
            .Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.PublicId == rolloutId && item.AdapterKind == "external", cancellationToken)
        ?? throw new TeamLabApiContractException("rollout_not_found", "未找到 TeamLab rollout", 404);

    private static string NormalizeExternalReference(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 256)
            throw new TeamLabApiContractException("rollout_reference_invalid", "rollout 的 external reference 无效", 422);
        return normalized;
    }

    private static IReadOnlyList<TeamLabRolloutTargetInputModel> NormalizeTargets(
        IReadOnlyList<TeamLabRolloutTargetInputModel> values)
    {
        if (values.Count > 1000)
            throw new TeamLabApiContractException("rollout_target_limit_exceeded", "单个 rollout 最多包含 1000 个 targets", 422);
        var result = new List<TeamLabRolloutTargetInputModel>(values.Count);
        var subjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var subject = value.ExternalSubject.Trim();
            var displayName = value.DisplayName.Trim();
            if (subject.Length is < 1 or > 256 || displayName.Length is < 1 or > 256)
                throw new TeamLabApiContractException("rollout_target_invalid", "rollout target 的 subject 或 display name 无效", 422);
            if (!subjects.Add(subject))
                throw new TeamLabApiContractException("rollout_target_duplicate", "rollout target 的 subject 重复", 422);
            result.Add(new TeamLabRolloutTargetInputModel(subject, displayName));
        }
        return result;
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
            throw new TeamLabApiContractException("rollout_cursor_invalid", "rollout target 的 cursor 无效", 400);
        }
    }

    private static string EncodeCursor(int id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString()));
}
