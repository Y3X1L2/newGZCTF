using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Identity.Contracts;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.League.Application;

public sealed class LeagueMatchService(AppDbContext db, LeagueMatchStore store, ITeamMembershipQuery teams,
    ITeamLabReleaseCatalog topologies, ILeagueRuntimePort runtime, ILeagueFlagPort flags,
    ILeagueCoinPort coins, IOptions<LeagueOptions> options) : ILeagueMatchQuery
{
    public void RequireEnabled()
    {
        if (!options.Value.Enabled) throw new LeagueException("league_disabled", "联赛入口尚未开放。", 503);
    }

    private void Authorize(ActorContext actor, bool admin = false)
    {
        RequireEnabled();
        if (actor.UserId is null || actor.IsApiToken) throw new LeagueException("league_login_required", "请使用网页登录。", 401);
        if (admin && actor.Role < Role.Admin) throw new LeagueException("league_forbidden", "需要管理员权限。", 403);
    }

    public async Task<LeagueMatchDetail> CreateAsync(LeagueDraftModel model, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        await ValidateDraftAsync(model, actor, ct);
        var match = new LeagueMatch { Name = model.Name.Trim(), TopologyId = model.TopologyId,
            ReleaseId = model.ReleaseId, InitialCoins = model.InitialCoins, CreatedById = actor.UserId!.Value };
        db.Add(match);
        await db.SaveChangesAsync(ct);
        return await GetAsync(match.Id, actor, ct);
    }

    private async Task ValidateDraftAsync(LeagueDraftModel model, ActorContext actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Length > 160 || model.InitialCoins < 0 ||
            model.TopologyId.HasValue != model.ReleaseId.HasValue)
            throw new LeagueException("league_invalid_configuration", "请填写场次名称、完整场景版本和非负整数金币。", 422);
        if (model.TopologyId is { } topology && model.ReleaseId is { } release)
        {
            if (!await topologies.IsAvailableAsync(topology, release, ct))
                throw new LeagueException("league_release_unavailable", "场景版本不存在或已归档。", 422);
        }
    }

    public async Task<LeagueMatchPage> ListAsync(ActorContext actor, Guid? after, int limit, CancellationToken ct)
    {
        Authorize(actor);
        if (limit is < 1 or > 100) throw new LeagueException("league_invalid_limit", "每页数量应为 1 至 100。", 422);
        var query = db.Set<LeagueMatch>().AsNoTracking();
        if (after is { } cursor) query = query.Where(x => x.Id.CompareTo(cursor) > 0);
        var rows = await query.OrderBy(x => x.Id).Take(limit + 1).ToArrayAsync(ct);
        return new(rows.Take(limit).Select(LeagueMatchStore.Summary).ToArray(), rows.Length > limit ? rows[limit - 1].Id : null);
    }

    public async Task<LeagueMatchDetail> GetAsync(Guid id, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor);
        var m = await db.Set<LeagueMatch>().AsNoTracking().Include(x => x.Registrations).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LeagueException("league_not_found", "场次不存在。", 404);
        var admin = actor.Role >= Role.Admin;
        var currentTeams = m.PreparationId is null ? await teams.GetUserTeamsAsync(actor.UserId!.Value, ct) : [];
        bool Own(LeagueRegistration r) => m.PreparationId is null ? currentTeams.Contains(r.TeamId) : r.MemberIds.Contains(actor.UserId!.Value);
        var registrations = m.Registrations.Where(x => admin || x.Selected || Own(x))
            .OrderBy(x => x.TeamId).Select(x => new LeagueRegistrationModel(x.TeamId, x.TeamName, x.State,
                x.Selected, x.Seat, admin || Own(x) ? x.MemberIds : [])).ToArray();
        var actions = new List<string>();
        if (m.State == LeagueMatchState.Draft)
        {
            actions.Add("register");
            if (admin) actions.AddRange(["edit", "review", "selectTeams", "prepare"]);
        }
        if (admin && m.State == LeagueMatchState.Ready) actions.Add("start");
        if (admin && m.State != LeagueMatchState.Ended) actions.Add("abort");
        if (admin && (m.State is LeagueMatchState.Preparing or LeagueMatchState.Starting) &&
            m.OperationState == LeagueProgressState.Failed && m.Retryable) actions.Add("retry");
        if (admin && m.State == LeagueMatchState.Ended && m.CleanupOperationId.HasValue &&
            m.CleanupState == LeagueProgressState.Failed && m.CleanupRetryable) actions.Add("retryCleanup");
        var operationId = m.StartOperationId ?? m.PreparationId;
        return new(LeagueMatchStore.Summary(m), m.TopologyId, m.ReleaseId, m.InitialCoins, m.ConfigurationVersion,
            registrations, m.Registrations.Where(x => x.Selected).OrderBy(x => x.TeamId).Select(x =>
            {
                var progress = LeagueMatchStore.Preparation(x);
                return admin || Own(x) ? progress : progress with { Binding = null };
            }).ToArray(), operationId is { } op ? new(op, m.OperationState, m.Failure, m.Retryable, m.AttemptCount) : null,
            m.CleanupOperationId is { } cleanup ? new(cleanup, m.CleanupState, m.CleanupFailure, m.CleanupRetryable, m.CleanupAttemptCount) : null,
            actions);
    }

    public async Task<LeagueMatchDetail> UpdateAsync(Guid id, LeagueUpdateDraftModel model, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        await ValidateDraftAsync(model.Draft, actor, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct);
        LeagueMatchStore.RequireDraft(m);
        LeagueMatchStore.RequireRevision(m, model.Revision);
        m.Name = model.Draft.Name.Trim(); m.TopologyId = model.Draft.TopologyId;
        m.ReleaseId = model.Draft.ReleaseId; m.InitialCoins = model.Draft.InitialCoins; m.Revision++;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchDetail> RegisterAsync(Guid id, int teamId, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct);
        var team = await teams.GetAsync(teamId, ct);
        if (team is null || team.CaptainId != actor.UserId || team.Locked)
            throw new LeagueException("league_captain_required", "仅未锁定战队的队长可以报名。", 403);
        // Replaying a successful registration never mutates a frozen match.
        if (m.Registrations.All(x => x.TeamId != teamId))
        {
            LeagueMatchStore.RequireDraft(m);
            m.Registrations.Add(new() { MatchId = id, TeamId = teamId, TeamName = team.Name, RegisteredById = actor.UserId!.Value });
            m.Revision++;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchDetail> ReviewAsync(Guid id, int teamId, LeagueRegistrationState state, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        if (state is not (LeagueRegistrationState.Approved or LeagueRegistrationState.Rejected))
            throw new LeagueException("league_invalid_review", "审核结果只能为通过或拒绝。", 422);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct); LeagueMatchStore.RequireDraft(m);
        var r = m.Registrations.SingleOrDefault(x => x.TeamId == teamId)
            ?? throw new LeagueException("league_registration_not_found", "未找到报名。", 404);
        if (r.State != state)
        {
            r.State = state; r.ReviewedById = actor.UserId; r.ReviewedAt = DateTimeOffset.UtcNow;
            if (state == LeagueRegistrationState.Rejected) { r.Selected = false; r.Seat = null; }
            m.Revision++; await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchDetail> SelectTeamsAsync(Guid id, LeagueSelectTeamsModel model, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct); LeagueMatchStore.RequireDraft(m); LeagueMatchStore.RequireRevision(m, model.Revision);
        var ids = new[] { model.FirstTeamId, model.SecondTeamId };
        if (ids.Distinct().Count() != 2 || m.Registrations.Count(x => ids.Contains(x.TeamId) && x.State == LeagueRegistrationState.Approved) != 2)
            throw new LeagueException("league_two_approved_teams_required", "请选择两支不同且审核通过的战队。", 422);
        // Clear old assignments first so swapping seats cannot violate the unique index mid-update.
        foreach (var registration in m.Registrations) { registration.Selected = false; registration.Seat = null; }
        await db.SaveChangesAsync(ct);
        foreach (var registration in m.Registrations.Where(x => ids.Contains(x.TeamId)))
        { registration.Selected = true; registration.Seat = registration.TeamId == model.FirstTeamId ? 1 : 2; }
        m.Revision++; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchDetail> PrepareAsync(Guid id, int revision, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct);
        if (m.PreparationId is null)
        {
            LeagueMatchStore.RequireDraft(m); LeagueMatchStore.RequireRevision(m, revision);
            if (!runtime.IsAvailable || !flags.IsAvailable || !coins.IsAvailable)
                throw new LeagueException("league_dependency_unavailable", "运行、Flag 或金币服务尚未接入。", 503);
            if (m.TopologyId is null || m.ReleaseId is null || m.Registrations.Count(x => x.Selected && x.State == LeagueRegistrationState.Approved) != 2)
                throw new LeagueException("league_incomplete_configuration", "请配置场景版本并选择两支审核通过的战队。", 422);
            await ValidateDraftAsync(new(m.Name, m.TopologyId, m.ReleaseId, m.InitialCoins), actor, ct);
            var members = new HashSet<Guid>();
            foreach (var r in m.Registrations.Where(x => x.Selected))
            {
                var team = await teams.GetAsync(r.TeamId, ct);
                if (team is null || team.Locked || team.MemberIds.Count == 0)
                    throw new LeagueException("league_team_unavailable", "参赛战队不可用。", 422);
                if (team.MemberIds.Any(member => !members.Add(member)))
                    throw new LeagueException("league_roster_overlap", "同一账号不能同时参加本场两队。", 422);
                r.MemberIds = team.MemberIds.ToArray(); r.TeamName = team.Name;
            }
            m.PreparationId = Guid.CreateVersion7(); m.ConfigurationVersion = m.Revision;
            m.State = LeagueMatchState.Preparing; m.OperationState = LeagueProgressState.Pending;
            m.NextAttemptAt = DateTimeOffset.UtcNow; m.Revision++;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchDetail> StartAsync(Guid id, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct);
        if (m.StartOperationId is null)
        {
            if (m.State != LeagueMatchState.Ready) throw new LeagueException("league_not_ready", "双方尚未就绪，不能开赛。");
            m.StartOperationId = Guid.CreateVersion7(); m.State = LeagueMatchState.Starting;
            m.OperationState = LeagueProgressState.Pending; m.AttemptCount = 0; m.Failure = LeagueFailure.None;
            m.Retryable = true; m.NextAttemptAt = DateTimeOffset.UtcNow; m.Revision++;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchDetail> AbortAsync(Guid id, string reason, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new LeagueException("league_abort_reason_required", "请填写 1 至 500 字的中止原因。", 422);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct);
        if (m.State != LeagueMatchState.Ended)
        {
            LeagueFinalizationService.End(m, null, null, LeagueEndReason.Aborted, actor.UserId!.Value);
            m.AbortReason = reason.Trim();
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchDetail> RetryAsync(Guid id, bool cleanup, ActorContext actor, CancellationToken ct)
    {
        Authorize(actor, true);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(id, ct);
        if (cleanup)
        {
            if (m.State != LeagueMatchState.Ended || m.CleanupOperationId is null || !m.CleanupRetryable)
                throw new LeagueException("league_not_retryable", "清理当前不可重试。");
            if (m.CleanupState == LeagueProgressState.Failed) { m.CleanupState = LeagueProgressState.Pending; m.CleanupFailure = LeagueFailure.None; }
        }
        else
        {
            if (m.State is not (LeagueMatchState.Preparing or LeagueMatchState.Starting) || !m.Retryable)
                throw new LeagueException("league_not_retryable", "操作当前不可重试。");
            if (m.OperationState == LeagueProgressState.Failed) { m.OperationState = LeagueProgressState.Pending; m.Failure = LeagueFailure.None; }
        }
        m.NextAttemptAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<LeagueMatchSummary> GetSummaryAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await db.Set<LeagueMatch>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new LeagueException("league_not_found", "场次不存在。", 404);
        return LeagueMatchStore.Summary(match);
    }

    public async Task<LeagueFrozenMatch> GetFrozenAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var m = await db.Set<LeagueMatch>().AsNoTracking().Include(x => x.Registrations).SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new LeagueException("league_not_found", "场次不存在。", 404);
        return LeagueMatchStore.Frozen(m);
    }

    public async Task<int?> GetParticipantTeamAsync(Guid matchId, Guid userId, CancellationToken cancellationToken) =>
        await db.Set<LeagueRegistration>().Where(x => x.MatchId == matchId && x.Selected && x.MemberIds.Contains(userId))
            .Select(x => (int?)x.TeamId).SingleOrDefaultAsync(cancellationToken);
}
