using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(ChallengeId))]
public class FlagContext
{
    internal static FlagContext CreateInstanceFlag(string flag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);
        return new FlagContext
        {
            Flag = flag,
            IsOccupied = true
        };
    }

    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Flag content
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxFlagLength)]
    public string Flag { get; set; } = string.Empty;

    /// <summary>
    /// Whether it is occupied
    /// </summary>
    public bool IsOccupied { get; set; }

    /// <summary>
    /// Order index for multi-flag challenges
    /// </summary>
    public int OrderIndex { get; set; }

    /// <summary>
    /// Description of this flag/answer
    /// </summary>
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>
    /// Score mode for this flag
    /// </summary>
    public FlagScoreMode ScoreMode { get; set; } = FlagScoreMode.InheritDecay;

    /// <summary>
    /// Fixed score value (used when ScoreMode is Fixed)
    /// </summary>
    public int FixedScore { get; set; }

    /// <summary>
    /// Maximum submission attempts for this flag
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    /// SHA256 hash of the attachment file
    /// </summary>
    [MaxLength(128)]
    public string? AttachmentHash { get; set; }

    /// <summary>
    /// Type of answer expected for this flag
    /// </summary>
    public AnswerType AnswerType { get; set; } = AnswerType.Flag;

    /// <summary>
    /// Custom display name for this flag
    /// </summary>
    [MaxLength(64)]
    public string? CustomName { get; set; }

    #region Db Relationship

    /// <summary>
    /// Attachment ID
    /// </summary>
    public int? AttachmentId { get; set; }

    /// <summary>
    /// Attachment
    /// </summary>
    public Attachment? Attachment { get; set; }

    /// <summary>
    /// Challenge ID
    /// </summary>
    public int? ChallengeId { get; set; }

    /// <summary>
    /// Challenge
    /// </summary>
    public GameChallenge? Challenge { get; set; }

    /// <summary>
    /// Exercise ID
    /// </summary>
    public int? ExerciseId { get; set; }

    /// <summary>
    /// Exercise
    /// </summary>
    public ExerciseChallenge? Exercise { get; set; }

    #endregion Db Relationship
}
