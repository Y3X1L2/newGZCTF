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
            foreach (var service in services)
            {
                var participation = await context.Participations
                    .FirstOrDefaultAsync(p => p.TeamId == part.TeamId && p.GameId == game.Id, token);
                if (participation is null) continue;

                // SLA score: checker status OK earns SLA points
                var task = checkerTasks.FirstOrDefault(t => t.ServiceId == service.Id && t.TeamId == part.TeamId);
                if (task?.Status == CheckerStatus.OK)
                {
                    var slaSubmission = new Submission
                    {
                        GameId = game.Id,
                        TeamId = part.TeamId,
                        ChallengeId = service.Id,
                        Answer = $"SLA-{service.Name}-R{round.RoundNumber}",
                        Status = AnswerResult.Accepted,
                        SubmitTimeUtc = DateTimeOffset.UtcNow,
                        SubmissionType = ScoringSubmissionType.Flag,
                        Score = service.SlaPoints,
                        ParticipationId = participation.Id
                    };
                    await context.Submissions.AddAsync(slaSubmission, token);
                }

                // Defense lost: each stolen flag deducts attack points from the victim
                var stolenFlags = flags.Where(f => f.ServiceId == service.Id && f.TeamId == part.TeamId && f.IsSubmitted);
                var attackCount = Math.Min(stolenFlags.Count(), service.MaxAttackPerRound);
                if (attackCount > 0)
                {
                    var lostScore = -(service.AttackPoints * attackCount);
                    var lostSubmission = new Submission
                    {
                        GameId = game.Id,
                        TeamId = part.TeamId,
                        ChallengeId = service.Id,
                        Answer = $"DEF-LOST-{service.Name}-R{round.RoundNumber}",
                        Status = AnswerResult.Accepted,
                        SubmitTimeUtc = DateTimeOffset.UtcNow,
                        SubmissionType = ScoringSubmissionType.Flag,
                        Score = lostScore,
                        ParticipationId = participation.Id
                    };
                    await context.Submissions.AddAsync(lostSubmission, token);
                }
            }
        }

        await context.SaveChangesAsync(token);
        logger.LogInformation("AWD round {RoundId} scores calculated", round.Id);
    }

    public async Task RecordFlagSubmission(int gameId, int attackerTeamId, AwdFlag flag, AwdService service, CancellationToken token = default)
    {
        var score = service.AttackPoints;

        // Mark flag as submitted
        flag.IsSubmitted = true;
        flag.FirstSubmittedAt = DateTimeOffset.UtcNow;
        context.AwdFlags.Update(flag);

        // Create Submission for attack score
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

        // Create or update FirstSolve
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

        // Get victim team name for event
        var victimTeam = await context.Teams.FindAsync([flag.TeamId], token);
        var victimTeamName = victimTeam?.Name ?? "Unknown";

        // Fix: Values format = [points, victimTeam, serviceName]
        await eventRepository.AddEvent(new GameEvent
        {
            GameId = gameId,
            TeamId = attackerTeamId,
            Type = EventType.AwdFlagSubmit,
            Values = [$"+{score} pts", victimTeamName, service.Name]
        }, token);
    }
}
