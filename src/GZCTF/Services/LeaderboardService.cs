using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

/// <summary>
/// Leaderboard calculation service. Computes ranked standings with per-dimension score breakdown.
/// Results are cached in Redis for real-time access.
/// </summary>
public class LeaderboardService
{
    private readonly AppDbContext _context;
    private readonly ScoringService _scoring;
    private readonly ILogger<LeaderboardService> _logger;

    public LeaderboardService(AppDbContext context, ScoringService scoring, ILogger<LeaderboardService> logger)
    {
        _context = context;
        _scoring = scoring;
        _logger = logger;
    }

    /// <summary>
    /// Generates the leaderboard for a scenario/IR challenge with detail score breakdown.
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(int challengeId)
    {
        // Fetch Scenario instances
        var scenarioInstances = await _context.ScenarioInstances
            .AsNoTracking()
            .Where(i => i.ScenarioId == challengeId && i.Status != ScenarioInstanceStatus.Expired)
            .Select(i => new { i.Id, i.ScenarioId, i.UserId, i.Status })
            .ToListAsync();

        // Fetch IR instances
        var irInstances = await _context.Set<IRInstance>()
            .AsNoTracking()
            .Where(i => i.ChallengeId == challengeId
                && i.EnvironmentStatus != EnvironmentStatus.Destroyed
                && i.EnvironmentStatus != EnvironmentStatus.Error)
            .Select(i => new { i.Id, ScenarioId = i.ChallengeId, i.UserId })
            .ToListAsync();

        // Merge into a unified (UserId, ChallengeId) set
        var userIds = scenarioInstances.Select(i => i.UserId)
            .Union(irInstances.Select(i => i.UserId))
            .Distinct()
            .ToList();

        var entries = new List<LeaderboardEntry>();
        foreach (var userId in userIds)
        {
            var totalScore = await _scoring.CalculateTotalScoreAsync(challengeId, userId);
            var rules = await _context.ScoringRules
                .AsNoTracking()
                .Where(r => r.ChallengeId == challengeId)
                .ToListAsync();

            var detailScores = new Dictionary<string, int>();
            foreach (var rule in rules)
            {
                var best = await _context.Submissions
                    .AsNoTracking()
                    .Where(s => s.ChallengeId == challengeId
                        && s.UserId == userId
                        && s.SubmissionType == rule.SubmissionType
                        && s.Status == AnswerResult.Accepted)
                    .OrderByDescending(s => s.Score)
                    .Select(s => (int?)s.Score)
                    .FirstOrDefaultAsync();

                detailScores[rule.SubmissionType.ToString()] = best ?? 0;
            }

            entries.Add(new LeaderboardEntry
            {
                UserId = userId.ToString(),
                TotalScore = totalScore,
                DetailScores = detailScores
            });
        }

        return entries
            .OrderByDescending(e => e.TotalScore)
            .Select((e, i) => { e.Rank = i + 1; return e; })
            .ToList();
    }
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public Dictionary<string, int> DetailScores { get; set; } = [];

    // For JSON serialization - convert enum keys to strings
    public Dictionary<string, int> FlattenedDetailScores =>
        DetailScores.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
}
