using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class AwdScoreService(
    AppDbContext context,
    IAwdRepository awdRepository,
    IGameEventRepository eventRepository,
    ILogger<AwdScoreService> logger)
{
    public async Task CalculateRoundScores(AwdRound round, Game game, CancellationToken token = default)
    {
        var services = await awdRepository.GetServicesByGame(game.Id, token);
        var participations = game.Participations.Where(p => p.Status == ParticipationStatus.Accepted).ToList();
        var checkerTasks = await awdRepository.GetCheckerTasksByRound(round.Id, token);
        var flags = await context.AwdFlags
            .Where(f => f.RoundId == round.Id)
            .ToListAsync(token);

        foreach (var part in participations)
        {
            int attackScore = 0;
            int slaScore = 0;
            int defenseLost = 0;

            foreach (var service in services)
            {
                // SLA 分
                var task = checkerTasks.FirstOrDefault(t => t.ServiceId == service.Id && t.TeamId == part.TeamId);
                if (task?.Status == CheckerStatus.OK)
                    slaScore += service.SlaPoints;

                // 被攻击失分
                var serviceFlags = flags.Where(f => f.ServiceId == service.Id && f.TeamId == part.TeamId && f.IsSubmitted);
                var attackCount = Math.Min(serviceFlags.Count(), service.MaxAttackPerRound);
                defenseLost += attackCount * service.AttackPoints;
            }

            // 攻击分已经在 Flag 提交时计算，这里只记录 SLA 和防守失分
            // 实际实现中，攻击分通过 RecordFlagSubmission 实时写入
        }
    }

    public async Task RecordFlagSubmission(int gameId, int attackerTeamId, AwdFlag flag, AwdService service, CancellationToken token = default)
    {
        var score = service.AttackPoints;

        // 创建 Submission
        var participation = await context.Participations
            .FirstOrDefaultAsync(p => p.TeamId == attackerTeamId && p.GameId == gameId, token);

        if (participation is null)
        {
            logger.LogWarning("Participation not found for game {GameId}, team {TeamId}", gameId, attackerTeamId);
            return;
        }

        var submission = new Submission
        {
            GameId = gameId,
            TeamId = attackerTeamId,
            UserId = null,
            ChallengeId = flag.ServiceId,
            Answer = flag.FlagValue,
            Status = AnswerResult.Accepted,
            SubmitTimeUtc = DateTimeOffset.UtcNow,
            SubmissionType = ScoringSubmissionType.Flag,
            Score = score,
            ParticipationId = participation.Id
        };

        await context.Submissions.AddAsync(submission, token);
        await context.SaveChangesAsync(token);

        // 创建或更新 FirstSolve
        var existingFirstSolve = await context.FirstSolves
            .FindAsync([participation.Id, flag.ServiceId], token);

        if (existingFirstSolve is null)
        {
            context.FirstSolves.Add(new FirstSolve
            {
                ParticipationId = participation.Id,
                ChallengeId = flag.ServiceId,
                SubmissionId = submission.Id
            });
            await context.SaveChangesAsync(token);
        }

        await eventRepository.AddEvent(new GameEvent
        {
            GameId = gameId,
            TeamId = attackerTeamId,
            Type = EventType.AwdFlagSubmit,
            Values = [$"+{score} pts", $"Service: {service.Name}"]
        }, token);
    }
}
