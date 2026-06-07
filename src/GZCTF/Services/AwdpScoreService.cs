using GZCTF.Models.Request.Game;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public record AwdpScoreBreakdown(
    int TeamId,
    int AttackScore,
    int SlaScore,
    int PatchScore,
    int PenaltyScore,
    DateTimeOffset LastScoreTime)
{
    public int TotalScore => AttackScore + SlaScore + PatchScore - PenaltyScore;
}

public class AwdpScoreService(AppDbContext context)
{
    public async Task<Dictionary<int, AwdpScoreBreakdown>> GetBreakdowns(int gameId,
        CancellationToken token = default)
    {
        var services = await context.AwdpServices.AsNoTracking()
            .Where(s => s.GameId == gameId)
            .ToDictionaryAsync(s => s.Id, token);

        var teamIds = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .Select(p => p.TeamId)
            .ToArrayAsync(token);

        var mutable = teamIds.ToDictionary(id => id, id => new MutableBreakdown(id));

        if (services.Count == 0 || mutable.Count == 0)
            return mutable.ToDictionary(kv => kv.Key, kv => kv.Value.ToRecord());

        var flags = await context.AwdpFlags.AsNoTracking()
            .Include(f => f.Round)
            .Where(f => f.Round.GameId == gameId && f.IsSubmitted && f.SubmittedByTeamId != null)
            .Select(f => new
            {
                f.ServiceId,
                f.SubmittedByTeamId,
                f.FirstSubmittedAt
            })
            .ToArrayAsync(token);

        foreach (var flag in flags)
        {
            if (flag.SubmittedByTeamId is not { } teamId || !mutable.TryGetValue(teamId, out var item))
                continue;

            if (!services.TryGetValue(flag.ServiceId, out var service))
                continue;

            item.AttackScore += service.AttackPoints;
            item.Touch(flag.FirstSubmittedAt);
        }

        var checkerTasks = await context.AwdpCheckerTasks.AsNoTracking()
            .Include(t => t.Round)
            .Where(t => t.Round.GameId == gameId)
            .Select(t => new
            {
                t.ServiceId,
                t.TeamId,
                t.Status,
                t.ExecutedAt
            })
            .ToArrayAsync(token);

        foreach (var task in checkerTasks)
        {
            if (task.Status != CheckerStatus.OK || !mutable.TryGetValue(task.TeamId, out var item))
                continue;

            if (!services.TryGetValue(task.ServiceId, out var service))
                continue;

            item.SlaScore += service.SlaPoints;
            item.Touch(task.ExecutedAt);
        }

        var patches = await context.AwdpPatchSubmissions.AsNoTracking()
            .Include(p => p.Round)
            .Where(p => p.Round.GameId == gameId)
            .Select(p => new
            {
                p.ServiceId,
                p.TeamId,
                p.FinalStatus,
                p.SubmittedAt
            })
            .ToArrayAsync(token);

        foreach (var patch in patches)
        {
            if (!mutable.TryGetValue(patch.TeamId, out var item) ||
                !services.TryGetValue(patch.ServiceId, out var service))
                continue;

            switch (patch.FinalStatus)
            {
                case AwdpPatchStatus.ExpFailed:
                    item.PatchScore += service.PatchPoints;
                    item.Touch(patch.SubmittedAt);
                    break;
                case AwdpPatchStatus.CheckerFailed:
                    item.PenaltyScore += service.ServiceAbnormalPenalty;
                    item.Touch(patch.SubmittedAt);
                    break;
            }
        }

        return mutable.ToDictionary(kv => kv.Key, kv => kv.Value.ToRecord());
    }

    public async Task<AwdpScoreboardItem[]> GetScoreboard(int gameId, IReadOnlyDictionary<int, int>? ctfScores = null,
        CancellationToken token = default)
    {
        var teams = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .Include(p => p.Team)
            .Select(p => new
            {
                p.TeamId,
                TeamName = p.Team.Name
            })
            .ToArrayAsync(token);

        var breakdowns = await GetBreakdowns(gameId, token);

        var rows = teams.Select(team =>
        {
            var breakdown = breakdowns.GetValueOrDefault(team.TeamId) ?? new AwdpScoreBreakdown(
                team.TeamId, 0, 0, 0, 0, DateTimeOffset.MinValue);

            return new AwdpScoreboardItem
            {
                TeamId = team.TeamId,
                TeamName = team.TeamName,
                CtfScore = ctfScores?.GetValueOrDefault(team.TeamId) ?? 0,
                AwdpScore = breakdown.TotalScore,
                AttackScore = breakdown.AttackScore,
                SlaScore = breakdown.SlaScore,
                PatchScore = breakdown.PatchScore,
                PenaltyScore = breakdown.PenaltyScore
            };
        }).OrderByDescending(i => i.TotalScore).ThenBy(i => i.TeamName).ToArray();

        for (var i = 0; i < rows.Length; i++)
            rows[i].Rank = i + 1;

        return rows;
    }

    sealed class MutableBreakdown(int teamId)
    {
        public int AttackScore { get; set; }
        public int SlaScore { get; set; }
        public int PatchScore { get; set; }
        public int PenaltyScore { get; set; }
        DateTimeOffset LastScoreTime { get; set; } = DateTimeOffset.MinValue;

        public void Touch(DateTimeOffset? time)
        {
            if (time is { } value && value > LastScoreTime)
                LastScoreTime = value;
        }

        public AwdpScoreBreakdown ToRecord() =>
            new(teamId, AttackScore, SlaScore, PatchScore, PenaltyScore, LastScoreTime);
    }
}
