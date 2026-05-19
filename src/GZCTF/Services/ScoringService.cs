using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

/// <summary>
/// Multi-dimension scoring engine that calculates weighted composite scores
/// based on admin-configured scoring rules. Supports automatic flag verification,
/// score decay strategies, and manual review scoring.
/// </summary>
public class ScoringService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ScoringService> _logger;

    public ScoringService(AppDbContext context, ILogger<ScoringService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Calculates the total score for a team/user on a specific challenge
    /// by aggregating all submission types according to their weight rules.
    /// </summary>
    public async Task<int> CalculateTotalScoreAsync(int challengeId, Guid userId)
    {
        var rules = await _context.ScoringRules
            .Where(r => r.ChallengeId == challengeId)
            .ToListAsync();

        if (rules.Count == 0)
        {
            _logger.LogDebug("No scoring rules for challenge {ChallengeId}, returning 0", challengeId);
            return 0;
        }

        var submissions = await _context.Submissions
            .Where(s => s.ChallengeId == challengeId && s.UserId == userId
                && s.Status == AnswerResult.Accepted)
            .ToListAsync();

        var totalScore = 0m;
        foreach (var rule in rules)
        {
            var typeSubmissions = submissions
                .Where(s => s.SubmissionType == rule.SubmissionType)
                .OrderByDescending(s => s.Score)  // ★ Best score wins — already decayed
                .ToList();

            var bestScore = typeSubmissions.FirstOrDefault()?.Score ?? 0;
            totalScore += bestScore * rule.Weight / 100m;
        }

        return (int)Math.Round(totalScore, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Validates that scoring rule weights sum to exactly 100%.
    /// </summary>
    public bool ValidateWeights(IEnumerable<ScoringRule> rules)
    {
        var sum = rules.Sum(r => r.Weight);
        return Math.Abs(sum - 100m) < 0.01m;
    }

}
