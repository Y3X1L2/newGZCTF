using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

public sealed class PenetrationTeamLabAdapter(
    AppDbContext context,
    ITeamLabRuntimeApplicationService runtimes,
    ITeamLabTopologyApplicationService topologies,
    ITeamLabControlPlaneOperationService operations,
    PenetrationObjectiveService objectives,
    ITeamLabRolloutApplicationService? rolloutApplication = null,
    IDistributedLeaseProvider? locks = null)
    : ITeamLabRolloutTargetProvider
{
    private readonly ITeamLabRolloutApplicationService _rollouts =
        rolloutApplication ?? new TeamLabRolloutApplicationService(context);
    private readonly IDistributedLeaseProvider? _locks = locks;

    public string AdapterKind => "penetration";

    public async Task<PenetrationGameLabBindingModel?> GetBindingAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken);
        if (binding is null) return null;
        var topologyId = (await topologies.GetStorageReferenceAsync(binding.TopologyId, cancellationToken)).Id;
        return new PenetrationGameLabBindingModel(
            gameId, topologyId, binding.ActiveReleaseId, binding.MaxResetCount,
            binding.ObjectiveRevision,
            await objectives.ListAsync(gameId, cancellationToken));
    }

    public async Task<PenetrationGameLabBindingModel> BindAsync(
        int gameId,
        Guid topologyPublicId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        await using var lease = _locks is null ? null : await _locks.AcquireAsync(
            PenetrationObjectiveService.ConfigurationLeaseKey(gameId), TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);
        using var leaseCancellation = lease is null ? null : CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lease.LeaseLost);
        if (leaseCancellation is not null) cancellationToken = leaseCancellation.Token;

        var topologyId = (await topologies.GetStorageReferenceAsync(
            topologyPublicId, actorUserId, administrator, cancellationToken)).StorageId;
        var binding = await context.PenetrationGameLabBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken);
        if (binding is null)
        {
            binding = new PenetrationGameLabBinding { GameId = gameId, TopologyId = topologyId };
            context.PenetrationGameLabBindings.Add(binding);
        }
        else if (binding.TopologyId != topologyId)
        {
            if (binding.ActiveRolloutId is { } rolloutId)
            {
                var active = await _rollouts.GetByStorageIdAsync(rolloutId, cancellationToken);
                if (active is not null && active.Status != "completed")
                    throw new TeamLabApiContractException(
                        "rollout_active", "Drain the active rollout before rebinding the topology.", 409);
            }
            var hasRuntime = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
                .AnyAsync(item => item.GameId == gameId &&
                                  item.Status != PenetrationRuntimeBindingStatus.Destroyed,
                    cancellationToken);
            if (hasRuntime) throw new InvalidOperationException("Destroy existing team runtimes before rebinding the topology.");
            var oldObjectives = await context.PenetrationObjectives
                .Where(item => item.GameId == gameId)
                .ToArrayAsync(cancellationToken);
            if (oldObjectives.Length > 0)
            {
                var oldObjectiveIds = oldObjectives.Select(item => item.Id).ToArray();
                var hasSubmissions = await context.PenetrationSubmissions.AsNoTracking()
                    .AnyAsync(item => oldObjectiveIds.Contains(item.ObjectiveId), cancellationToken);
                if (hasSubmissions)
                    throw new TeamLabApiContractException(
                        "objective_history_exists",
                        "A game with scoring submissions cannot be rebound to another topology.", 409);
                context.PenetrationObjectives.RemoveRange(oldObjectives);
            }
            binding.TopologyId = topologyId;
            binding.ActiveReleaseId = null;
            binding.ActiveRolloutId = null;
            binding.ObjectiveRevision++;
        }
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return (await GetBindingAsync(gameId, cancellationToken))!;
    }

    public async Task<PenetrationGameLabBindingModel> ActivateReleaseAsync(
        int gameId,
        Guid releasePublicId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        await using var lease = _locks is null ? null : await _locks.AcquireAsync(
            PenetrationObjectiveService.ConfigurationLeaseKey(gameId), TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);
        using var leaseCancellation = lease is null ? null : CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lease.LeaseLost);
        if (leaseCancellation is not null) cancellationToken = leaseCancellation.Token;

        var binding = await context.PenetrationGameLabBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game has no TeamLab topology binding.");
        var topology = await topologies.GetStorageReferenceAsync(binding.TopologyId, cancellationToken);
        var release = await topologies.GetReleaseAsync(
            topology.Id, releasePublicId, actorUserId, administrator, cancellationToken);
        if (binding.ActiveRolloutId is { } activeRolloutId)
        {
            var active = await _rollouts.GetByStorageIdAsync(activeRolloutId, cancellationToken)
                ?? throw new TeamLabApiContractException("rollout_not_found", "未找到 TeamLab rollout。", 404);
            if (active.Status != "completed" && active.ReleaseId != release.Id)
                throw new InvalidOperationException("Drain the active rollout before selecting another release.");
            if (active.ReleaseId != release.Id) binding.ActiveRolloutId = null;
        }
        binding.ActiveReleaseId = release.Id;
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return (await GetBindingAsync(gameId, cancellationToken))!;
    }

    public async Task<(int Created, int Reused)> DeployGameAsync(
        int gameId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var teamIds = await context.Participations.AsNoTracking()
            .Where(item => item.GameId == gameId && item.Status == ParticipationStatus.Accepted)
            .Select(item => item.TeamId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var created = 0;
        var reused = 0;
        foreach (var teamId in teamIds.Order())
        {
            var result = await DeployTeamAsync(gameId, teamId, actorUserId, cancellationToken);
            if (result.Reused) reused++;
            else created++;
        }
        return (created, reused);
    }

    public async Task<IReadOnlyList<PenetrationReleaseOptionModel>> ListAvailableReleasesAsync(
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var available = await topologies.ListAsync(actorUserId, administrator, cancellationToken);
        var result = new List<PenetrationReleaseOptionModel>();
        foreach (var topology in available.OrderBy(item => item.Name))
        foreach (var release in (await topologies.ListReleasesAsync(
                     topology.Id, actorUserId, administrator, cancellationToken)).OrderByDescending(item => item.Version))
        {
            var detail = await topologies.GetAsync(topology.Id, actorUserId, administrator, cancellationToken);
            result.Add(new PenetrationReleaseOptionModel(topology.Id, topology.Name, release.Id, release.Version,
                detail.Definition.Networks.Count, detail.Definition.Assets.Count, release.PublishedAt));
        }
        return result;
    }

    public async Task<PenetrationGameTeamLabModel> GetGameTeamLabAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var binding = await GetBindingAsync(gameId, cancellationToken);
        if (binding is null) return new PenetrationGameTeamLabModel(null, null);
        var rolloutId = await ActiveRolloutPublicIdAsync(gameId, cancellationToken);
        return new PenetrationGameTeamLabModel(
            binding,
            rolloutId is null ? null : await _rollouts.GetAsync(rolloutId.Value, cancellationToken));
    }

    public async Task<TeamLabRolloutModel> PrepareAsync(
        int gameId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        await using var lease = _locks is null ? null : await _locks.AcquireAsync(
            PenetrationObjectiveService.ConfigurationLeaseKey(gameId), TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);
        using var leaseCancellation = lease is null ? null : CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lease.LeaseLost);
        if (leaseCancellation is not null) cancellationToken = leaseCancellation.Token;
        var rollout = await EnsureRolloutAsync(gameId, actorUserId, administrator, cancellationToken);
        await operations.SubmitRolloutPrepareAsync(null, actorUserId,
            $"penetration:{gameId}:prepare:{rollout.Revision}", rollout.Id,
            RequireScope(rollout), cancellationToken);
        return rollout;
    }

    public async Task<TeamLabRolloutModel> SetAccessAsync(
        int gameId,
        Guid actorUserId,
        bool administrator,
        bool open,
        CancellationToken cancellationToken)
    {
        await using var lease = _locks is null ? null : await _locks.AcquireAsync(
            PenetrationObjectiveService.ConfigurationLeaseKey(gameId), TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);
        using var leaseCancellation = lease is null ? null : CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lease.LeaseLost);
        if (leaseCancellation is not null) cancellationToken = leaseCancellation.Token;
        var rollout = await EnsureRolloutAsync(gameId, actorUserId, administrator, cancellationToken);
        await operations.SubmitRolloutSetAccessAsync(null, actorUserId,
            $"penetration:{gameId}:access:{open}:{rollout.Revision}", rollout.Id,
            RequireScope(rollout), open, cancellationToken);
        return rollout;
    }

    public async Task<TeamLabRolloutModel> DrainAsync(
        int gameId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        await using var lease = _locks is null ? null : await _locks.AcquireAsync(
            PenetrationObjectiveService.ConfigurationLeaseKey(gameId), TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);
        using var leaseCancellation = lease is null ? null : CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lease.LeaseLost);
        if (leaseCancellation is not null) cancellationToken = leaseCancellation.Token;
        var rollout = await EnsureRolloutAsync(gameId, actorUserId, administrator, cancellationToken);
        await operations.SubmitRolloutDrainAsync(null, actorUserId,
            $"penetration:{gameId}:drain:{rollout.Revision}", rollout.Id,
            RequireScope(rollout), cancellationToken);
        return rollout;
    }

    public async Task<TeamLabRolloutModel> PauseAsync(
        int gameId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var rollout = await EnsureRolloutAsync(gameId, actorUserId, administrator, cancellationToken);
        await operations.SubmitRolloutPauseAsync(null, actorUserId,
            $"penetration:{gameId}:pause:{rollout.Revision}", rollout.Id,
            RequireScope(rollout), cancellationToken);
        return rollout;
    }

    public async Task<TeamLabRolloutModel> ResumeAsync(
        int gameId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var rollout = await EnsureRolloutAsync(gameId, actorUserId, administrator, cancellationToken);
        await operations.SubmitRolloutResumeAsync(null, actorUserId,
            $"penetration:{gameId}:resume:{rollout.Revision}", rollout.Id,
            RequireScope(rollout), cancellationToken);
        return rollout;
    }

    public async Task<TeamLabRolloutTargetPageModel> ListRolloutTargetsAsync(
        int gameId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var rolloutId = await ActiveRolloutPublicIdAsync(gameId, cancellationToken)
            ?? throw new TeamLabApiContractException("rollout_not_found", "The game has no active TeamLab rollout.", 404);
        return await _rollouts.ListTargetsAsync(rolloutId, after, limit, cancellationToken);
    }

    public async Task<bool> IsPlayerAccessOpenAsync(int gameId, CancellationToken cancellationToken)
    {
        var rolloutId = await context.PenetrationGameLabBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => item.ActiveRolloutId)
            .SingleOrDefaultAsync(cancellationToken);
        if (rolloutId is null) return false;
        var rollout = await _rollouts.GetByStorageIdAsync(rolloutId.Value, cancellationToken);
        return rollout is { Status: "ready", DesiredAccessOpen: true, DrainRequested: false };
    }

    public async Task SynchronizeTargetsAsync(TeamLabRollout rollout, CancellationToken cancellationToken)
    {
        var gameId = ParseGameReference(rollout.ExternalReference);
        var game = await context.Games.AsNoTracking()
            .Where(item => item.Id == gameId)
            .Select(item => new { item.EndTimeUtc, item.PracticeMode })
            .SingleOrDefaultAsync(cancellationToken);
        if (game is null || !game.PracticeMode && game.EndTimeUtc <= DateTimeOffset.UtcNow)
        {
            rollout.DesiredAccessOpen = false;
            rollout.DrainRequested = true;
            rollout.Status = TeamLabRolloutStatus.Draining;
            rollout.DrainingAt ??= DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return;
        }
        var accepted = await context.Participations.AsNoTracking()
            .Where(item => item.GameId == gameId && item.Status == ParticipationStatus.Accepted)
            .Select(item => new { item.TeamId, item.Team.Name })
            .Distinct()
            .OrderBy(item => item.TeamId)
            .ToArrayAsync(cancellationToken);
        var existing = rollout.Targets.ToDictionary(item => item.ExternalSubject, StringComparer.Ordinal);
        var desiredSubjects = accepted.Select(item => $"team:{item.TeamId}").ToHashSet(StringComparer.Ordinal);
        foreach (var target in rollout.Targets)
        {
            target.IsDesired = desiredSubjects.Contains(target.ExternalSubject);
            target.UpdatedAt = DateTimeOffset.UtcNow;
        }
        foreach (var team in accepted)
        {
            var subject = $"team:{team.TeamId}";
            if (existing.TryGetValue(subject, out var target))
            {
                if (target.DisplayName != team.Name) target.DisplayName = team.Name;
                continue;
            }
            rollout.Targets.Add(new TeamLabRolloutTarget
            {
                RolloutId = rollout.Id,
                ExternalSubject = subject,
                DisplayName = team.Name
            });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<TeamLabRolloutProvisionResult> ProvisionAsync(
        TeamLabRollout rollout,
        TeamLabRolloutTarget target,
        CancellationToken cancellationToken)
    {
        var gameId = ParseGameReference(rollout.ExternalReference);
        var teamId = ParseTeamSubject(target.ExternalSubject);
        var result = await DeployTeamCoreAsync(
            gameId, teamId, rollout.CreatedByUserId, $"rollout-target:{target.PublicId:D}", cancellationToken);
        return new TeamLabRolloutProvisionResult(result.RuntimeId, result.RuntimePublicId, null);
    }

    public async Task<TeamLabRuntimeCreateResult> DeployTeamAsync(
        int gameId,
        int teamId,
        Guid actorUserId,
        CancellationToken cancellationToken)
        => await DeployTeamCoreAsync(gameId, teamId, actorUserId, null, cancellationToken);

    private async Task<TeamLabRuntimeCreateResult> DeployTeamCoreAsync(
        int gameId,
        int teamId,
        Guid actorUserId,
        string? creationIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game has no TeamLab topology binding.");
        if (binding.ActiveReleaseId is not { } releaseId)
            throw new InvalidOperationException("Publish and activate a TeamLab topology release before deployment.");
        var runtimeOwnerUserId = (await topologies.GetStorageReferenceAsync(binding.TopologyId, cancellationToken)).OwnerUserId
            ?? throw new InvalidOperationException("The bound TeamLab topology has no owner.");
        var overlays = await objectives.BuildOverlaysAsync(gameId, teamId, releaseId, cancellationToken);
        var command = new CreateTeamLabRuntimeModel(
            releaseId, $"penetration:{gameId}:team:{teamId}", null, overlays);
        var result = await runtimes.PlanAndEnqueueAsync(
            command, actorUserId, runtimeOwnerUserId, Hash(command), creationIdempotencyKey, null,
            $"game {gameId} / team {teamId}", cancellationToken);
        var runtimeBinding = await context.PenetrationTeamRuntimeBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == teamId, cancellationToken);
        if (runtimeBinding is null)
        {
            context.PenetrationTeamRuntimeBindings.Add(new PenetrationTeamRuntimeBinding
            {
                GameId = gameId,
                TeamId = teamId,
                RuntimeId = result.RuntimeId
            });
        }
        else
        {
            runtimeBinding.RuntimeId = result.RuntimeId;
            runtimeBinding.Status = PenetrationRuntimeBindingStatus.Active;
            runtimeBinding.DestroyOperationId = null;
            runtimeBinding.DestroyedAt = null;
        }
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<TeamLabRuntimeCreateResult> ResetTeamAsync(
        int gameId,
        int teamId,
        Guid userId,
        bool byAdmin,
        CancellationToken cancellationToken)
    {
        var binding = await LoadRuntimeBindingAsync(gameId, teamId, cancellationToken);
        if (binding.Status != PenetrationRuntimeBindingStatus.Active)
            throw new InvalidOperationException("The TeamLab runtime is being destroyed or has already been destroyed.");
        var settings = await context.PenetrationGameLabBindings.AsNoTracking()
            .SingleAsync(item => item.GameId == gameId, cancellationToken);
        var runtime = await runtimes.GetByStorageIdAsync(binding.RuntimeId, cancellationToken);
        if (settings.ActiveReleaseId is not { } releaseId)
            throw new InvalidOperationException("The game has no active TeamLab release.");
        var overlays = await objectives.BuildOverlaysAsync(gameId, teamId, releaseId, cancellationToken);
        var reset = await ReserveResetAsync(
            binding.RuntimeId, userId, byAdmin, settings.MaxResetCount, cancellationToken);
        try
        {
            var command = new ResetTeamLabRuntimeModel(overlays, releaseId);
            var rolloutId = await ActiveRolloutStorageIdAsync(gameId, cancellationToken);
            return rolloutId is { } activeRolloutId
                ? await runtimes.ResetRolloutTargetAndEnqueueAsync(
                    runtime.Id, activeRolloutId, command, reset.OperationId, cancellationToken)
                : await runtimes.ResetAndEnqueueAsync(runtime.Id, command, reset.OperationId, cancellationToken);
        }
        catch
        {
            await ReleaseFailedResetReservationAsync(reset.OperationId, cancellationToken);
            throw;
        }
    }

    public async Task DestroyTeamAsync(int gameId, int teamId, CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            var lockKey = $"penetration-destroy:{gameId}:{teamId}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }

        var binding = await LoadRuntimeBindingAsync(gameId, teamId, cancellationToken);
        if (binding.Status == PenetrationRuntimeBindingStatus.Destroyed)
            return;
        var runtime = await runtimes.GetByStorageIdAsync(binding.RuntimeId, cancellationToken);
        var operationId = binding.DestroyOperationId ?? Guid.CreateVersion7();
        if (binding.Status != PenetrationRuntimeBindingStatus.Destroying ||
            binding.DestroyOperationId is null)
        {
            binding.Status = PenetrationRuntimeBindingStatus.Destroying;
            binding.DestroyOperationId = operationId;
            await context.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        var rolloutId = await ActiveRolloutStorageIdAsync(gameId, cancellationToken);
        if (rolloutId is { } activeRolloutId)
        {
            var actorUserId = await RuntimeOwnerIdAsync(binding.RuntimeId, cancellationToken);
            await runtimes.DestroyRolloutTargetAndEnqueueAsync(
                runtime.Id, activeRolloutId, operationId, actorUserId, cancellationToken);
            return;
        }
        await runtimes.DestroyAndEnqueueAsync(runtime.Id, operationId, null, cancellationToken);
    }

    public async Task DestroyGameAsync(int gameId, CancellationToken cancellationToken)
    {
        var teams = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => item.TeamId)
            .ToArrayAsync(cancellationToken);
        foreach (var teamId in teams) await DestroyTeamAsync(gameId, teamId, cancellationToken);
    }

    public async Task EnsureGameDrainedBeforeDeleteAsync(
        int gameId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var rolloutId = await ActiveRolloutPublicIdAsync(gameId, cancellationToken);
        if (rolloutId is null) return;
        var rollout = await _rollouts.GetAsync(rolloutId.Value, cancellationToken);
        if (rollout is null || rollout.Status is "completed" or "archived") return;
        if (!rollout.DrainRequested)
            await operations.SubmitRolloutDrainAsync(null, actorUserId,
                $"penetration:{gameId}:delete-drain:{rollout.Revision}", rollout.Id,
                RequireScope(rollout), cancellationToken);
        throw new TeamLabApiContractException(
            "rollout_not_drained", "比赛组网环境正在清理；全部资源清理完成后才能删除比赛。", 409);
    }

    public async Task<IReadOnlyList<PenetrationRuntimeBindingModel>> ListRuntimesAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var rows = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => new
            {
                item.TeamId,
                TeamName = context.Teams.Where(team => team.Id == item.TeamId).Select(team => team.Name).First(),
                item.RuntimeId
            }).OrderBy(item => item.TeamName)
            .ToArrayAsync(cancellationToken);
        var result = new List<PenetrationRuntimeBindingModel>(rows.Length);
        foreach (var item in rows)
        {
            var runtime = await runtimes.GetByStorageIdAsync(item.RuntimeId, cancellationToken);
            result.Add(new PenetrationRuntimeBindingModel(
                item.TeamId, item.TeamName, runtime.Id, runtime.Generation, runtime.Status,
                runtime.Stage, runtime.Shards.Count, runtime.Assets.Count, runtime.CreatedAt,
                runtime.UpdatedAt, runtime.Error));
        }
        return result;
    }

    private async Task<PenetrationTeamRuntimeBinding> LoadRuntimeBindingAsync(
        int gameId,
        int teamId,
        CancellationToken cancellationToken) =>
        await context.PenetrationTeamRuntimeBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == teamId, cancellationToken)
        ?? throw new InvalidOperationException("The team has no TeamLab runtime binding.");

    private async Task<PenetrationResetRecord> ReserveResetAsync(
        int runtimeId,
        Guid userId,
        bool byAdmin,
        int maxResetCount,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            var lockKey = $"penetration-reset:{runtimeId}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }

        var targetGeneration = (await runtimes.GetByStorageIdAsync(runtimeId, cancellationToken)).Generation + 1;
        var activeResetExists = await context.PenetrationResetRecords.AsNoTracking()
            .AnyAsync(item => item.RuntimeId == runtimeId &&
                              item.TargetGeneration == targetGeneration &&
                              (item.Status == PenetrationResetStatus.Pending ||
                               item.Status == PenetrationResetStatus.Running), cancellationToken);
        if (activeResetExists)
            throw new InvalidOperationException("A reset is already pending for this runtime generation.");

        if (!byAdmin)
        {
            var consumed = await context.PenetrationResetRecords.AsNoTracking()
                .CountAsync(item => item.RuntimeId == runtimeId && !item.ByAdmin &&
                                    (item.Status == PenetrationResetStatus.Pending ||
                                     item.Status == PenetrationResetStatus.Running ||
                                     item.Status == PenetrationResetStatus.Succeeded ||
                                     item.Status == PenetrationResetStatus.Failed &&
                                     item.FailureClass == PenetrationResetFailureClass.Scenario),
                    cancellationToken);
            if (consumed >= maxResetCount)
                throw new InvalidOperationException("Reset limit reached.");
        }

        var record = new PenetrationResetRecord
        {
            RuntimeId = runtimeId,
            UserId = userId,
            ByAdmin = byAdmin,
            OperationId = Guid.CreateVersion7(),
            TargetGeneration = targetGeneration,
            Status = PenetrationResetStatus.Pending,
            ResetAt = DateTimeOffset.UtcNow
        };
        context.PenetrationResetRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return record;
    }

    private async Task ReleaseFailedResetReservationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var record = await context.PenetrationResetRecords.SingleAsync(
            item => item.OperationId == operationId, cancellationToken);
        record.Status = PenetrationResetStatus.Failed;
        record.FailureClass = PenetrationResetFailureClass.Infrastructure;
        record.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(CreateTeamLabRuntimeModel command)
    {
        var json = JsonSerializer.Serialize(command);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private async Task<TeamLabRolloutModel> EnsureRolloutAsync(
        int gameId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken)
            ?? throw new TeamLabApiContractException(
                "binding_not_found", "Bind a TeamLab release before preparing the game.", 409);
        if (binding.ActiveReleaseId is not { } releaseId)
            throw new TeamLabApiContractException(
                "release_not_selected", "Select a published TeamLab release first.", 409);
        var topology = await topologies.GetStorageReferenceAsync(binding.TopologyId, cancellationToken);
        var ownerId = topology.OwnerUserId;
        if (!administrator && ownerId != actorUserId)
            throw new TeamLabApiContractException(
                "insufficient_permission", "The bound topology is not managed by this user.", 403);
        var rollout = await _rollouts.EnsureAsync(
            releaseId, ownerId ?? actorUserId, actorUserId, AdapterKind,
            $"penetration-game:{gameId}", cancellationToken);
        var rolloutKey = await _rollouts.GetStorageIdAsync(rollout.Id, cancellationToken);
        if (binding.ActiveRolloutId != rolloutKey)
        {
            binding.ActiveRolloutId = rolloutKey;
            binding.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        return rollout;
    }

    private async Task<Guid?> ActiveRolloutPublicIdAsync(int gameId, CancellationToken cancellationToken)
    {
        var storageId = await context.PenetrationGameLabBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => item.ActiveRolloutId)
            .SingleOrDefaultAsync(cancellationToken);
        return storageId is null
            ? null
            : (await _rollouts.GetByStorageIdAsync(storageId.Value, cancellationToken))?.Id;
    }

    private Task<int?> ActiveRolloutStorageIdAsync(int gameId, CancellationToken cancellationToken) =>
        context.PenetrationGameLabBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => item.ActiveRolloutId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<Guid> RuntimeOwnerIdAsync(int runtimeId, CancellationToken cancellationToken)
    {
        var ownerId = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.Id == runtimeId)
            .Select(item => item.CreatedById)
            .SingleAsync(cancellationToken);
        return ownerId ?? throw new TeamLabApiContractException(
            "runtime_owner_missing", "该 TeamLab 运行环境缺少所有者，无法执行比赛生命周期操作。", 409);
    }

    private static int ParseGameReference(string reference) =>
        reference.StartsWith("penetration-game:", StringComparison.Ordinal) &&
        int.TryParse(reference[17..], out var gameId)
            ? gameId
            : throw new InvalidOperationException("The penetration rollout game reference is invalid.");

    private static Guid RequireScope(TeamLabRolloutModel rollout) =>
        rollout.ControlScopeId ?? throw new TeamLabApiContractException(
            "teamlab_scope_missing", "TeamLab rollout 缺少控制范围。", 409);

    private static int ParseTeamSubject(string subject) =>
        subject.StartsWith("team:", StringComparison.Ordinal) && int.TryParse(subject[5..], out var teamId)
            ? teamId
            : throw new InvalidOperationException("The penetration rollout team subject is invalid.");
}
