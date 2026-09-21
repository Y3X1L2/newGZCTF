using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;

namespace GZCTF.Modules.League.Application;

public sealed class LeagueFinalizationService(AppDbContext db, LeagueMatchStore store) : ILeagueFinalizationService
{
    public async Task<LeagueFinalizationResult> ConfirmKnockoutAsync(LeagueKnockoutCommand command, CancellationToken cancellationToken)
    {
        var m = await store.LockAsync(command.MatchId, cancellationToken);
        var winner = m.Registrations.SingleOrDefault(x => x.Selected && x.TeamId == command.WinnerTeamId && x.MemberIds.Contains(command.UserId));
        if (winner is null) throw new LeagueException("league_not_participant", "账号不在本场固定名单中。", 403);
        if (LeagueMatchStore.Result(m) is { } existing) return new(false, existing);
        if (m.State != LeagueMatchState.Running) throw new LeagueException("league_not_running", "场次尚未开赛。");
        var target = m.Registrations.Single(x => x.Selected && x.TeamId != winner.TeamId);
        if (command.SubmissionId == Guid.Empty || m.PreparationId != command.PreparationId ||
            target.TeamId != command.Target.TeamId || target.RuntimeId != command.Target.RuntimeId || target.Generation != command.Target.Generation)
            throw new LeagueException("league_binding_mismatch", "提交与本场对方核心运行身份不匹配。");
        End(m, winner.TeamId, command.SubmissionId, LeagueEndReason.Knockout, command.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new(true, LeagueMatchStore.Result(m)!);
    }

    internal static void End(LeagueMatch m, int? winner, Guid? submission, LeagueEndReason reason, Guid actor)
    {
        m.State = LeagueMatchState.Ended; m.WinnerTeamId = winner; m.WinningSubmissionId = submission;
        m.EndReason = reason; m.EndedById = actor;
        m.EndedAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); m.Revision++;
        // No runtime was requested for an unprepared draft.
        if (m.PreparationId.HasValue)
        {
            m.CleanupOperationId = Guid.CreateVersion7(); m.CleanupState = LeagueProgressState.Pending;
            m.NextAttemptAt = DateTimeOffset.UtcNow;
        }
    }
}
