using GZCTF.Controllers;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Scoring;

/// <summary>
/// Single canonical scoring pipeline for ALL challenge types.
/// Every verification path in the system must delegate to this engine.
/// Key invariants:
///   1. ScoreDecay is applied ONCE, here, at Submission creation time.
///   2. ScoringService reads already-decayed Submission.Score -- never re-applies decay.
///   3. Every score-affecting event (flag accepted, checkpoint passed, stage completed)
///      writes a Submission record (ensures leaderboard visibility).
/// </summary>
public class UnifiedScoringEngine
{
    private readonly AppDbContext _context;
    private readonly ILogger<UnifiedScoringEngine> _logger;
    private readonly Dictionary<VerificationMode, IVerificationStrategy> _strategies;

    public UnifiedScoringEngine(
        AppDbContext context,
        ILogger<UnifiedScoringEngine> logger,
        IEnumerable<IVerificationStrategy> strategies)
    {
        _context = context;
        _logger = logger;
        _strategies = new Dictionary<VerificationMode, IVerificationStrategy>();
        foreach (var s in strategies)
            _strategies[s.HandledMode] = s;
    }

    /// <summary>
    /// Primary submission pipeline. Called by SubmissionController for all multi-type submissions.
    /// Verifies answer, applies score decay (ONCE), writes Submission record.
    /// </summary>
    public async Task<VerificationResult> ProcessSubmissionAsync(
        SubmissionCreateRequest request, Guid userId, CancellationToken token)
    {
        // 1. Find scoring rule
        var rule = await _context.ScoringRules
            .FirstOrDefaultAsync(r => r.ChallengeId == request.ChallengeId
                && r.SubmissionType == request.SubmissionType, token);

        if (rule is null)
        {
            _logger.LogWarning("No ScoringRule for Challenge {ChallengeId}, Type {SubmissionType}",
                request.ChallengeId, request.SubmissionType);
            return new VerificationResult(AnswerResult.NotFound, 0);
        }

        // 2. Check attempt limits
        if (rule.MaxAttempts > 0)
        {
            var count = await _context.Submissions.CountAsync(
                s => s.ChallengeId == request.ChallengeId
                    && s.UserId == userId
                    && s.SubmissionType == request.SubmissionType, token);
            if (count >= rule.MaxAttempts)
                return new VerificationResult(AnswerResult.WrongAnswer, 0);
        }

        // 3. Select verification strategy by VerificationMode
        if (!_strategies.TryGetValue(rule.VerificationMode, out var strategy))
        {
            _logger.LogWarning("No strategy for VerificationMode {Mode} on Rule {RuleId}",
                rule.VerificationMode, rule.Id);
            return new VerificationResult(AnswerResult.FlagSubmitted, 0);
        }

        // 4. Execute verification
        var result = await strategy.VerifyAsync(request.Answer, rule, _context, token);

        // 5. Apply score decay ONCE -- this is the ONLY place decay is applied
        var attemptCount = await _context.Submissions.CountAsync(
            s => s.ChallengeId == request.ChallengeId
                && s.UserId == userId
                && s.SubmissionType == request.SubmissionType, token);
        var decayedScore = ScoreDecayCalculator.Apply(result.Score, attemptCount, rule.ScoreDecay);

        // 6. Write Submission record -- ensures leaderboard visibility
        var submission = new Submission
        {
            Answer = request.Answer,
            Status = result.Status,
            SubmissionType = request.SubmissionType,
            Content = request.Content,
            AttemptNumber = attemptCount + 1,
            Score = decayedScore,
            SubmitTimeUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            ChallengeId = request.ChallengeId,
            GameId = request.GameId,
            TeamId = request.TeamId,
            ParticipationId = request.ParticipationId
        };

        _context.Submissions.Add(submission);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation("Submission {Id}: Type={Type} Status={Status} Score={Score} User={UserId}",
            submission.Id, request.SubmissionType, result.Status, decayedScore, userId);

        return new VerificationResult(result.Status, decayedScore);
    }

    /// <summary>
    /// Records an IR checkpoint completion as a Submission.
    /// FIXES P0 BUG: IR scores now appear on leaderboard.
    /// </summary>
    public async Task RecordIRCheckpointCompletionAsync(
        int challengeId, Guid userId, int gameId, int teamId, int participationId,
        CancellationToken token)
    {
        var challenge = await _context.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == challengeId, token);
        if (challenge is null) return;

        // Write as Flag-type Submission for leaderboard compatibility
        var submission = new Submission
        {
            Answer = $"ir-completion:{challengeId}",
            Status = AnswerResult.Accepted,
            SubmissionType = ScoringSubmissionType.Flag,
            AttemptNumber = 1,
            Score = challenge.OriginalScore,
            SubmitTimeUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            ChallengeId = challengeId,
            GameId = gameId,
            TeamId = teamId,
            ParticipationId = participationId
        };
        _context.Submissions.Add(submission);
        await _context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Records a Scenario stage completion as a Submission.
    /// </summary>
    public async Task RecordStageCompletionAsync(
        int challengeId, int stageId, Guid userId, int gameId, int teamId,
        int participationId, CancellationToken token)
    {
        var stage = await _context.Stages.FindAsync([stageId], cancellationToken: token);
        if (stage is null) return;

        var submission = new Submission
        {
            Answer = $"stage:{stageId}",
            Status = AnswerResult.Accepted,
            SubmissionType = ScoringSubmissionType.Flag,
            AttemptNumber = 1,
            Score = 100,
            SubmitTimeUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            ChallengeId = challengeId,
            GameId = gameId,
            TeamId = teamId,
            ParticipationId = participationId
        };
        _context.Submissions.Add(submission);
        await _context.SaveChangesAsync(token);
    }
}
