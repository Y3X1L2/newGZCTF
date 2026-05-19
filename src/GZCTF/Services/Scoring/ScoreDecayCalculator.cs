namespace GZCTF.Services.Scoring;

/// <summary>
/// Canonical score decay calculator.
/// CRITICAL: This is the ONLY place score decay is applied in the entire system.
/// ScoringService reads already-decayed scores from Submission.Score and does NOT re-apply decay.
/// </summary>
public static class ScoreDecayCalculator
{
    /// <param name="baseScore">Original score before decay</param>
    /// <param name="attemptIndex">0-based attempt number (first attempt = 0)</param>
    /// <param name="decay">Decay strategy</param>
    public static int Apply(int baseScore, int attemptIndex, ScoreDecay decay)
    {
        if (attemptIndex < 0) return baseScore;
        if (baseScore <= 0) return 0;
        return decay switch
        {
            ScoreDecay.None => baseScore,
            ScoreDecay.Half => attemptIndex == 0
                ? baseScore
                : baseScore / (1 << attemptIndex),
            ScoreDecay.Linear => Math.Max(0, baseScore - attemptIndex * 10),
            _ => baseScore
        };
    }
}
