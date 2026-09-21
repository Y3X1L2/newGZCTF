using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.League.Application;

public sealed class LeagueException(string code, string message, int statusCode = 409)
    : ApiContractException(code, message, statusCode);

public sealed class LeagueOptions
{
    public bool Enabled { get; set; }
}

public sealed class LeagueMatchStore(AppDbContext db)
{
    public async Task<LeagueMatch> LockAsync(Guid id, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("League match writes require the caller's transaction.");
        // Always refresh: an earlier query in this scope may predate a competing terminal write.
        var match = await db.Set<LeagueMatch>().FromSqlInterpolated($"SELECT * FROM \"LeagueMatches\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new LeagueException("league_not_found", "场次不存在。", 404);
        await db.Entry(match).ReloadAsync(ct);
        await db.Entry(match).Collection(x => x.Registrations).Query().LoadAsync(ct);
        foreach (var registration in match.Registrations)
            await db.Entry(registration).ReloadAsync(ct);
        return match;
    }

    public static LeagueFrozenMatch Frozen(LeagueMatch m) => m.PreparationId is { } preparationId &&
        m.TopologyId is { } topologyId && m.ReleaseId is { } releaseId && m.ConfigurationVersion is { } version
        ? new(m.Id, version, preparationId, topologyId, releaseId, m.InitialCoins,
            m.Registrations.Where(x => x.Selected).OrderBy(x => x.Seat)
                .Select(x => new LeagueTeamSnapshot(x.TeamId, x.TeamName, x.Seat!.Value, x.MemberIds)).ToArray())
        : throw new LeagueException("league_not_prepared", "场次配置尚未固定。");

    public static LeagueResultModel? Result(LeagueMatch m) => m.EndedAt is { } endedAt && m.EndReason is { } reason
        ? new(m.WinnerTeamId, reason, m.WinningSubmissionId, endedAt, m.AbortReason) : null;
    public static LeagueMatchSummary Summary(LeagueMatch m) => new(m.Id, m.Name, m.State, m.Revision, m.CreatedAt, m.StartedAt, Result(m));
    public static LeagueTeamPreparation Preparation(LeagueRegistration r) => new(r.TeamId, r.PreparationState,
        r.RuntimeId is { } runtime && r.Generation is { } generation ? new(r.TeamId, runtime, generation) : null,
        r.EnvironmentReady, r.FlagInjected, r.EntryPrepared, r.AccessClosed, r.Failure, r.Retryable);
    public static void RequireDraft(LeagueMatch m)
    {
        if (m.State != LeagueMatchState.Draft) throw new LeagueException("league_configuration_frozen", "准备后不能修改报名和配置。");
    }
    public static void RequireRevision(LeagueMatch m, int revision)
    {
        if (m.Revision != revision) throw new LeagueException("league_revision_conflict", "场次已更新，请刷新后重试。");
    }
}
