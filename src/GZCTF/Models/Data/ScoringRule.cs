using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GZCTF.Models.Data;

/// <summary>
/// Type of submission content expected for a scoring rule
/// </summary>
public enum ScoringSubmissionType : byte
{
    Flag = 0,
    Writeup = 1,
    IP = 2,
    Credential = 3,
    Custom = 4
}

public enum VerificationMode : byte
{
    AutoExact = 0,
    AutoRegex = 1,
    AutoScript = 2,
    ManualReview = 3
}

public enum ScoreDecay : byte
{
    None = 0,
    Half = 1,
    Linear = 2
}

public class ScoringRule
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Challenge ID this scoring rule applies to
    /// </summary>
    [Required]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Type of submission expected
    /// </summary>
    [Required]
    public ScoringSubmissionType SubmissionType { get; set; }

    /// <summary>
    /// Weight of this rule (0-100) in the overall challenge score
    /// </summary>
    [Required]
    [Range(0, 100)]
    public decimal Weight { get; set; }

    /// <summary>
    /// How submissions are verified
    /// </summary>
    [Required]
    public VerificationMode VerificationMode { get; set; }

    /// <summary>
    /// Maximum number of attempts allowed (0 = unlimited)
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    /// Score decay method for repeated submissions
    /// </summary>
    [Required]
    public ScoreDecay ScoreDecay { get; set; } = ScoreDecay.None;

    /// <summary>
    /// SHA256 hash of the expected answer for AutoExact/ManualAnswer verification
    /// </summary>
    [MaxLength(512)]
    public string? ExpectedAnswerHash { get; set; }

    /// <summary>
    /// JSON configuration for AutoRegex/AutoScript verification.
    /// AutoRegex: {"Pattern":"regex_pattern"}
    /// AutoScript: {"ScriptPath":"...", "Timeout":30}
    /// </summary>
    [MaxLength(2048)]
    public string? VerificationConfig { get; set; }

    #region Db Relationship

    /// <summary>
    /// Parent challenge
    /// </summary>
    [ForeignKey(nameof(ChallengeId))]
    public GameChallenge? Challenge { get; set; }

    #endregion
}
