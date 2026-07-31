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
    PenetrationObjectiveService objectives,
    ITeamLabRolloutApplicationService? rolloutApplication = null,
    IDistributedLeaseProvider? locks = null)
    : ITeamLabRolloutTargetProvider, ITeamLabRuntimeManagerAuthorizationProvider
{
    private readonly ITeamLabRolloutApplicationService _rollouts =
        rolloutApplication ?? new TeamLabRolloutApplicationService(context);
    private readonly IDistributedLeaseProvider? _locks = locks;

    public string AdapterKind => "penetration";

    public Task<bool> CanManageRuntimeAsync(
        int runtimeId,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.RuntimeId == runtimeId)
            .Join(
                context.Games.AsNoTracking(),
                binding => binding.GameId,
                game => game.Id,
                (_, game) => game.OwnerId)
            .AnyAsync(ownerId => ownerId == actorUserId, cancellationToken);

    public async Task<PenetrationGameLabBindingModel?> GetBindingAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken);
        if (binding is null) return null;
        var topologyId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.Id == binding.TopologyId)
            .Select(item => item.PublicId)
            .SingleAsync(cancellationToken);
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

        var topologyId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.PublicId == topologyPublicId &&
                           (administrator || item.OwnerUserId == actorUserId))
            .Select(item => (int?)item.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException(
                "topology_not_found", "The TeamLab topology was not found or is not managed by this user.", 404);
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
                var status = await context.TeamLabRollouts.AsNoTracking()
                    .Where(item => item.Id == rolloutId)
                    .Select(item => (TeamLabRolloutStatus?)item.Status)
                    .SingleOrDefaultAsync(cancellationToken);
                if (status is not null and not TeamLabRolloutStatus.Completed)
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
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releasePublicId && item.TopologyId == binding.TopologyId &&
                                          (administrator || item.Topology.OwnerUserId == actorUserId),
                cancellationToken)
            ?? throw new TeamLabApiContractException(
                "release_not_found", "The release does not belong to a managed topology.", 404);
        if (binding.ActiveRolloutId is { } activeRolloutId)
        {
            var active = await context.TeamLabRollouts.AsNoTracking()
                .SingleAsync(item => item.Id == activeRolloutId, cancellationToken);
            if (active.Status != TeamLabRolloutStatus.Completed && active.ReleaseId != release.Id)
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
        var query = context.TeamLabTopologyReleases.AsNoTracking();
        if (!administrator)
            query = query.Where(item => item.Topology.OwnerUserId == actorUserId);
        var rows = await query.OrderBy(item => item.Topology.Name).ThenByDescending(item => item.Version)
            .Select(item => new
            {
                item.Topology.PublicId,
                item.Topology.Name,
                item.Id,
                item.Version,
                NetworkCount = item.Topology.Networks.Count,
                AssetCount = item.Topology.Assets.Count,
                item.PublishedAt
            }).ToArrayAsync(cancellationToken);
        return rows.Select(item => new PenetrationReleaseOptionModel(
            item.PublicId, item.Name, item.Id, item.Version, item.NetworkCount, item.AssetCount, item.PublishedAt)).ToArray();
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
        return await _rollouts.RequestPreparationAsync(rollout.Id, cancellationToken);
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
        return await _rollouts.SetAccessAsync(rollout.Id, open, cancellationToken);
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
        return await _rollouts.RequestDrainAsync(rollout.Id, cancellationToken);
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

    public async Task<bool> IsPlayerAccessOpenAsync(int gameId, CancellationToken cancellationToken) =>
        await context.PenetrationGameLabBindings.AsNoTracking()
            .Where(item => item.GameId == gameId && item.ActiveRolloutId.HasValue)
            .AnyAsync(item => context.TeamLabRollouts.Any(rollout => rollout.Id == item.ActiveRolloutId &&
                rollout.Status == TeamLabRolloutStatus.Ready && rollout.DesiredAccessOpen &&
                !rollout.DrainRequested), cancellationToken);

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
        var existing = await context.TeamLabRolloutTargets
            .Where(item => item.RolloutId == rollout.Id)
            .ToDictionaryAsync(item => item.ExternalSubject, cancellationToken);
        foreach (var team in accepted)
        {
            var subject = $"team:{team.TeamId}";
            if (existing.TryGetValue(subject, out var target))
            {
                if (target.DisplayName != team.Name) target.DisplayName = team.Name;
                continue;
            }
            context.TeamLabRolloutTargets.Add(new TeamLabRolloutTarget
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
        var runtimeOwnerUserId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.Id == binding.TopologyId)
            .Select(item => item.OwnerUserId)
            .SingleAsync(cancellationToken)
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
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleAsync(item => item.Id == binding.RuntimeId, cancellationToken);
        if (settings.ActiveReleaseId is not { } releaseId)
            throw new InvalidOperationException("The game has no active TeamLab release.");
        var overlays = await objectives.BuildOverlaysAsync(gameId, teamId, releaseId, cancellationToken);
        var reset = await ReserveResetAsync(
            binding.RuntimeId, userId, byAdmin, settings.MaxResetCount, cancellationToken);
        try
        {
            return await runtimes.ResetAndEnqueueAsync(
                runtime.PublicId,
                new ResetTeamLabRuntimeModel(overlays, releaseId),
                reset.OperationId,
                cancellationToken);
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
        var runtimeId = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.Id == binding.RuntimeId)
            .Select(item => item.PublicId)
            .SingleAsync(cancellationToken);
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

        await runtimes.DestroyAndEnqueueAsync(runtimeId, operationId, null, cancellationToken);
    }

    public async Task DestroyGameAsync(int gameId, CancellationToken cancellationToken)
    {
        var teams = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => item.TeamId)
            .ToArrayAsync(cancellationToken);
        foreach (var teamId in teams) await DestroyTeamAsync(gameId, teamId, cancellationToken);
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
                Runtime = context.TeamLabRuntimes.Where(runtime => runtime.Id == item.RuntimeId).Select(runtime => new
                {
                    runtime.PublicId, runtime.Generation, runtime.Status,
                    ShardCount = runtime.Shards.Count(shard => shard.Generation == runtime.Generation),
                    AssetCount = runtime.Assets.Count(asset => asset.Generation == runtime.Generation),
                    runtime.CreatedAt, runtime.UpdatedAt, runtime.LastError
                }).First()
            }).OrderBy(item => item.TeamName)
            .ToArrayAsync(cancellationToken);
        return rows.Select(item => new PenetrationRuntimeBindingModel(
            item.TeamId, item.TeamName, item.Runtime.PublicId, item.Runtime.Generation, item.Runtime.Status,
            item.Runtime.Status.ToString().ToLowerInvariant(), item.Runtime.ShardCount, item.Runtime.AssetCount, item.Runtime.CreatedAt,
            item.Runtime.UpdatedAt, item.Runtime.LastError)).ToArray();
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

        var targetGeneration = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.Id == runtimeId)
            .Select(item => item.Generation + 1)
            .SingleAsync(cancellationToken);
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
        var ownerId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.Id == binding.TopologyId)
            .Select(item => item.OwnerUserId)
            .SingleAsync(cancellationToken);
        if (!administrator && ownerId != actorUserId)
            throw new TeamLabApiContractException(
                "insufficient_permission", "The bound topology is not managed by this user.", 403);
        var rollout = await _rollouts.EnsureAsync(
            releaseId, ownerId ?? actorUserId, actorUserId, AdapterKind,
            $"penetration-game:{gameId}", cancellationToken);
        var rolloutKey = await context.TeamLabRollouts.AsNoTracking()
            .Where(item => item.PublicId == rollout.Id)
            .Select(item => item.Id)
            .SingleAsync(cancellationToken);
        if (binding.ActiveRolloutId != rolloutKey)
        {
            binding.ActiveRolloutId = rolloutKey;
            binding.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        return rollout;
    }

    private async Task<Guid?> ActiveRolloutPublicIdAsync(
        int gameId,
        CancellationToken cancellationToken) =>
        await context.PenetrationGameLabBindings.AsNoTracking()
            .Where(item => item.GameId == gameId && item.ActiveRolloutId.HasValue)
            .Select(item => context.TeamLabRollouts.Where(rollout => rollout.Id == item.ActiveRolloutId)
                .Select(rollout => (Guid?)rollout.PublicId).Single())
            .SingleOrDefaultAsync(cancellationToken);

    private static int ParseGameReference(string reference) =>
        reference.StartsWith("penetration-game:", StringComparison.Ordinal) &&
        int.TryParse(reference[17..], out var gameId)
            ? gameId
            : throw new InvalidOperationException("The penetration rollout game reference is invalid.");

    private static int ParseTeamSubject(string subject) =>
        subject.StartsWith("team:", StringComparison.Ordinal) && int.TryParse(subject[5..], out var teamId)
            ? teamId
            : throw new InvalidOperationException("The penetration rollout team subject is invalid.");
}
