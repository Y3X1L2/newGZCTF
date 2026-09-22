using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.League.Infrastructure;

public sealed class LeagueTeamLabAdapter(
    AppDbContext db,
    ITeamLabRolloutApplicationService rollouts,
    ITeamLabRuntimeApplicationService runtimes,
    TeamLabAccessGrantService access,
    ILeagueFlagPort flags,
    ILeagueCoreMaterialPort materials)
    : ILeagueRuntimePort, ILeagueAttackAccessPort, ITeamLabRolloutTargetProvider
{
    public const string Kind = "league";
    public string AdapterKind => Kind;
    public bool IsAvailable => true;

    public async Task<LeaguePreparationResult> PrepareAsync(
        LeagueFrozenMatch match,
        IReadOnlyList<LeagueCoreReference> cores,
        CancellationToken cancellationToken)
    {
        if (!flags.IsAvailable || !materials.IsAvailable)
            throw new LeagueException("league_dependency_unavailable", "Flag 注入材料尚未接入。", 503);
        var rollout = await EnsureRolloutAsync(match, cancellationToken);
        var entity = await db.TeamLabRollouts.Include(item => item.Targets)
            .SingleAsync(item => item.PublicId == rollout.Id, cancellationToken);
        SynchronizeTargets(entity, match, cores);

        if (entity.Status is TeamLabRolloutStatus.Blocked or TeamLabRolloutStatus.Failed)
            foreach (var target in entity.Targets.Where(item => item.Status == TeamLabRolloutTargetStatus.Failed))
                if (target.RuntimeId is null)
                    target.Status = TeamLabRolloutTargetStatus.Pending;
                else
                    target.RebuildRequested = true;

        await db.SaveChangesAsync(cancellationToken);
        if (!entity.PreparationRequested && entity.Status != TeamLabRolloutStatus.Ready)
            await rollouts.RequestPreparationForOperationAsync(
                entity.PublicId, match.PreparationId, cancellationToken);

        return new(entity.PublicId, await ProjectPreparationAsync(entity.PublicId, match, cancellationToken));
    }

    public async Task<LeagueEffectResult> OpenAccessAsync(
        LeagueFrozenMatch match,
        Guid operationId,
        IReadOnlyList<LeagueRuntimeBinding> bindings,
        CancellationToken cancellationToken)
    {
        var rollout = await FindRolloutAsync(match, cancellationToken);
        if (rollout is null || !await BindingsMatchAsync(rollout.Id, bindings, cancellationToken))
            return new(false, LeagueFailure.AccessFailed, false);

        if (!rollout.DesiredAccessOpen)
            rollout = await rollouts.SetAccessForOperationAsync(
                rollout.Id, true, operationId, cancellationToken);

        var open = await db.TeamLabRolloutTargets.AsNoTracking()
            .Where(item => item.Rollout.PublicId == rollout.Id && item.IsDesired)
            .AllAsync(item => item.Status == TeamLabRolloutTargetStatus.AccessOpen, cancellationToken);
        return new(open && rollout.DesiredAccessOpen);
    }

    public async Task<LeagueEffectResult> CleanupAsync(
        LeagueFrozenMatch match,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rollout = await FindRolloutAsync(match, cancellationToken);
        if (rollout is null) return new(true);
        if (!rollout.DrainRequested)
            rollout = await rollouts.RequestDrainForOperationAsync(
                rollout.Id, operationId, cancellationToken);
        if (rollout.Status == "completed") return new(true);
        return rollout.Status is "failed" or "blocked"
            ? new(false, LeagueFailure.CleanupFailed, true)
            : new(false);
    }

    public async Task SynchronizeTargetsAsync(
        TeamLabRollout rollout,
        CancellationToken cancellationToken)
    {
        var (matchId, preparationId) = ParseReference(rollout.ExternalReference);
        var match = await db.Set<LeagueMatch>().Include(item => item.Registrations)
            .SingleOrDefaultAsync(item => item.Id == matchId, cancellationToken);
        if (match is null || match.PreparationId != preparationId || match.State == LeagueMatchState.Ended)
        {
            rollout.DesiredAccessOpen = false;
            rollout.DrainRequested = true;
            rollout.Status = TeamLabRolloutStatus.Draining;
            rollout.DrainingAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var frozen = LeagueMatchStore.Frozen(match);
        var cores = await flags.PrepareAsync(frozen, cancellationToken);
        SynchronizeTargets(rollout, frozen, cores);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TeamLabRolloutProvisionResult> ProvisionAsync(
        TeamLabRollout rollout,
        TeamLabRolloutTarget target,
        CancellationToken cancellationToken)
    {
        var (_, preparationId) = ParseReference(rollout.ExternalReference);
        var (teamId, materialId) = ParseSubject(target.ExternalSubject);
        var material = await materials.GetAsync(materialId, cancellationToken);
        if (material.MaterialId != materialId || string.IsNullOrWhiteSpace(material.AssetKey) ||
            string.IsNullOrWhiteSpace(material.SecretName))
            throw new LeagueException("league_core_material_invalid", "核心注入材料无效。", 422);

        var command = new CreateTeamLabRuntimeModel(
            rollout.ReleaseId,
            $"league:{preparationId:N}:team:{teamId}",
            null,
            [new TeamLabRuntimeOverlayModel(material.AssetKey,
                new Dictionary<string, string> { [material.SecretName] = material.Value })]);
        var result = await runtimes.PlanAndEnqueueAsync(
            command,
            rollout.CreatedByUserId,
            rollout.OwnerUserId,
            RequestHash(command),
            $"league-{preparationId:N}-team-{teamId}",
            null,
            target.DisplayName,
            cancellationToken);
        return new(result.RuntimeId, result.RuntimePublicId, null);
    }

    public async Task<LeagueAttackAccessGrant> CreateAsync(
        Guid matchId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var target = await ResolveOpponentAsync(matchId, userId, cancellationToken);
        var grant = await access.CreateAsync(target.RuntimeId, cancellationToken);
        var downloadToken = grant.ConfigurationDownloadUrl?.Split(
            "token=", 2, StringSplitOptions.None).ElementAtOrDefault(1);
        var downloadUrl = string.IsNullOrWhiteSpace(downloadToken)
            ? null
            : $"/api/league/matches/{matchId:D}/attack-access/{grant.Id:D}/download?token={Uri.EscapeDataString(downloadToken)}";
        return new(grant.Id, target.TeamId, target.TeamName, grant.ClientAddress, grant.Endpoint,
            grant.AllowedIps, grant.Dns, grant.CreatedAt, grant.ExpiresAt, downloadUrl);
    }

    public async Task<LeagueAccessConfiguration> ConsumeAsync(
        Guid matchId,
        Guid grantId,
        string token,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var target = await ResolveOpponentAsync(matchId, userId, cancellationToken);
        var configuration = await access.ConsumeConfigurationAsync(
            target.RuntimeId, grantId, token, cancellationToken);
        return new(configuration.FileName, configuration.Configuration);
    }

    private async Task<TeamLabRolloutModel> EnsureRolloutAsync(
        LeagueFrozenMatch match,
        CancellationToken cancellationToken)
    {
        var owner = await db.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == match.ReleaseId)
            .Select(item => item.Topology.OwnerUserId)
            .SingleAsync(cancellationToken);
        var creator = await db.Set<LeagueMatch>().AsNoTracking()
            .Where(item => item.Id == match.MatchId)
            .Select(item => item.CreatedById)
            .SingleAsync(cancellationToken);
        return await rollouts.EnsureAsync(
            match.ReleaseId,
            owner ?? creator,
            creator,
            Kind,
            Reference(match),
            cancellationToken);
    }

    private async Task<TeamLabRolloutModel?> FindRolloutAsync(
        LeagueFrozenMatch match,
        CancellationToken cancellationToken)
    {
        var rollout = await db.TeamLabRollouts.AsNoTracking().Include(item => item.Targets)
            .Where(item => item.ReleaseId == match.ReleaseId && item.AdapterKind == Kind &&
                           item.ExternalReference == Reference(match))
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return rollout is null ? null : TeamLabRolloutApplicationService.ToModel(rollout);
    }

    private async Task<IReadOnlyList<LeagueTeamPreparation>> ProjectPreparationAsync(
        Guid rolloutId,
        LeagueFrozenMatch match,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var facts = await db.TeamLabRolloutTargets.AsNoTracking()
            .Where(item => item.Rollout.PublicId == rolloutId && item.IsDesired)
            .Select(item => new TargetFact(
                item.ExternalSubject,
                item.Status,
                item.Runtime == null ? null : item.Runtime.PublicId,
                item.Runtime == null ? null : item.Runtime.Generation,
                item.Runtime == null ? null : item.Runtime.Status,
                item.Runtime != null && item.Runtime.PublicUdpMapping != null &&
                    item.Runtime.PublicUdpMapping.IsSynced,
                item.Runtime == null || !item.Runtime.IsOpenToPlayers &&
                    !item.Runtime.AccessGrants.Any(grant => grant.Generation == item.Runtime.Generation &&
                        grant.AppliedAt != null && !grant.Revoked && grant.ExpiresAt > now) &&
                    !item.Runtime.ServiceAccesses.Any(mapping => mapping.Generation == item.Runtime.Generation &&
                        mapping.RevokedAt == null)))
            .ToArrayAsync(cancellationToken);
        var byTeam = facts.ToDictionary(item => ParseSubject(item.Subject).TeamId);

        return match.Teams.Select(team =>
        {
            if (!byTeam.TryGetValue(team.TeamId, out var fact))
                return new LeagueTeamPreparation(team.TeamId, LeagueProgressState.Pending, null,
                    false, false, false, true);
            var ready = fact.TargetStatus is TeamLabRolloutTargetStatus.Ready or TeamLabRolloutTargetStatus.AccessOpen &&
                        fact.RuntimeStatus == TeamLabRuntimeStatus.Running && fact.EntryReady;
            var failed = fact.TargetStatus == TeamLabRolloutTargetStatus.Failed ||
                         fact.RuntimeStatus == TeamLabRuntimeStatus.Failed;
            var binding = fact.RuntimeId is { } runtimeId && fact.Generation is { } generation
                ? new LeagueRuntimeBinding(team.TeamId, runtimeId, generation)
                : null;
            return new LeagueTeamPreparation(
                team.TeamId,
                failed ? LeagueProgressState.Failed : ready ? LeagueProgressState.Ready : LeagueProgressState.Running,
                binding,
                ready,
                ready,
                fact.EntryReady,
                fact.AccessClosed,
                failed ? LeagueFailure.EnvironmentFailed : LeagueFailure.None,
                true);
        }).ToArray();
    }

    private async Task<bool> BindingsMatchAsync(
        Guid rolloutId,
        IReadOnlyList<LeagueRuntimeBinding> bindings,
        CancellationToken cancellationToken)
    {
        var actual = await db.TeamLabRolloutTargets.AsNoTracking()
            .Where(item => item.Rollout.PublicId == rolloutId && item.IsDesired && item.Runtime != null)
            .Select(item => new
            {
                item.ExternalSubject,
                RuntimeId = item.Runtime!.PublicId,
                item.Runtime.Generation
            }).ToArrayAsync(cancellationToken);
        return bindings.Count == actual.Length && bindings.All(binding => actual.Any(item =>
            ParseSubject(item.ExternalSubject).TeamId == binding.TeamId &&
            item.RuntimeId == binding.RuntimeId && item.Generation == binding.Generation));
    }

    private async Task<OpponentRuntime> ResolveOpponentAsync(
        Guid matchId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var match = await db.Set<LeagueMatch>().AsNoTracking().Include(item => item.Registrations)
            .SingleOrDefaultAsync(item => item.Id == matchId, cancellationToken)
            ?? throw new LeagueException("league_not_found", "场次不存在。", 404);
        if (match.State != LeagueMatchState.Running)
            throw new LeagueException("league_access_closed", "比赛尚未开放攻击接入。", 409);
        var selected = match.Registrations.Where(item => item.Selected).ToArray();
        var own = selected.SingleOrDefault(item => item.MemberIds.Contains(userId))
            ?? throw new LeagueException("league_forbidden", "你不是本场参赛成员。", 403);
        var opponent = selected.Single(item => item.TeamId != own.TeamId);
        if (opponent.RuntimeId is null)
            throw new LeagueException("league_environment_not_ready", "对方环境尚未就绪。", 409);
        var runtimeId = await db.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == opponent.RuntimeId)
            .Select(item => item.PublicId)
            .SingleAsync(cancellationToken);
        return new(runtimeId, opponent.TeamId, opponent.TeamName);
    }

    private static void SynchronizeTargets(
        TeamLabRollout rollout,
        LeagueFrozenMatch match,
        IReadOnlyList<LeagueCoreReference> cores)
    {
        var desired = match.Teams.ToDictionary(
            team => team.TeamId,
            team => Subject(team.TeamId, cores.Single(core => core.TeamId == team.TeamId).MaterialId));
        foreach (var target in rollout.Targets)
        {
            var teamId = ParseSubject(target.ExternalSubject).TeamId;
            if (!desired.TryGetValue(teamId, out var subject))
                target.IsDesired = false;
            else if (target.ExternalSubject != subject)
                throw new LeagueException("league_core_material_changed", "同一次准备的核心材料发生变化。", 409);
            else
                target.IsDesired = true;
        }
        foreach (var team in match.Teams.Where(team => rollout.Targets.All(target =>
                     ParseSubject(target.ExternalSubject).TeamId != team.TeamId)))
            rollout.Targets.Add(new TeamLabRolloutTarget
            {
                ExternalSubject = desired[team.TeamId],
                DisplayName = team.Name
            });
    }

    private static string Reference(LeagueFrozenMatch match) =>
        $"league:{match.MatchId:N}:preparation:{match.PreparationId:N}";

    private static (Guid MatchId, Guid PreparationId) ParseReference(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 4 || parts[0] != "league" || parts[2] != "preparation" ||
            !Guid.TryParseExact(parts[1], "N", out var matchId) ||
            !Guid.TryParseExact(parts[3], "N", out var preparationId))
            throw new InvalidOperationException("联赛 rollout 引用无效。");
        return (matchId, preparationId);
    }

    private static string Subject(int teamId, Guid materialId) => $"team:{teamId}:material:{materialId:N}";

    private static (int TeamId, Guid MaterialId) ParseSubject(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 4 || parts[0] != "team" || parts[2] != "material" ||
            !int.TryParse(parts[1], out var teamId) || teamId <= 0 ||
            !Guid.TryParseExact(parts[3], "N", out var materialId))
            throw new InvalidOperationException("联赛 rollout target 引用无效。");
        return (teamId, materialId);
    }

    private static string RequestHash(CreateTeamLabRuntimeModel command) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));

    private sealed record TargetFact(
        string Subject,
        TeamLabRolloutTargetStatus TargetStatus,
        Guid? RuntimeId,
        int? Generation,
        TeamLabRuntimeStatus? RuntimeStatus,
        bool EntryReady,
        bool AccessClosed);

    private sealed record OpponentRuntime(Guid RuntimeId, int TeamId, string TeamName);
}
