using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

/// <summary>
/// A verification checkpoint within an incident response challenge.
/// Defines what the player must accomplish and how it is verified.
/// </summary>
[Index(nameof(ChallengeId), nameof(OrderIndex))]
public class IRCheckpoint
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Parent IR challenge ID
    /// </summary>
    [Required]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Display order within the challenge
    /// </summary>
    [Required]
    public int OrderIndex { get; set; }

    /// <summary>
    /// Checkpoint description shown to the player
    /// </summary>
    [Required]
    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Score awarded for completing this checkpoint
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int Score { get; set; } = 100;

    /// <summary>
    /// Whether this checkpoint must be completed (if false, it is optional/bonus)
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// How this checkpoint is verified
    /// </summary>
    [Required]
    public VerificationType VerificationType { get; set; } = VerificationType.ManualAnswer;

    /// <summary>
    /// JSON configuration for verification.
    /// AutoCommand: {"Command":"...", "ExpectedOutput":"...", "MatchType":"Contains|Exact|Regex"}
    /// AutoScript:  {"ScriptPath":"...", "Timeout":30}
    /// ManualAnswer: {"ExpectedAnswer":"...", "CaseSensitive":false}
    /// ManualReview: {}
    /// </summary>
    public string? VerificationConfig { get; set; }

    #region Db Relationship

    /// <summary>
    /// Parent IR challenge
    /// </summary>
    [ForeignKey(nameof(ChallengeId))]
    public GameChallenge? Challenge { get; set; }

    #endregion
}

/// <summary>
/// A player's instance of an incident response challenge.
/// Tracks environment state, checkpoint progress, and shell activity.
/// </summary>
[Index(nameof(ChallengeId))]
[Index(nameof(UserId))]
[Index(nameof(TimeSlotId))]
[Index(nameof(EnvironmentStatus))]
public class IRInstance
{
    [Key]
    [Required]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Parent IR challenge ID
    /// </summary>
    [Required]
    public int ChallengeId { get; set; }

    /// <summary>
    /// User who owns this instance
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Current environment status
    /// </summary>
    [Required]
    public EnvironmentStatus EnvironmentStatus { get; set; } = EnvironmentStatus.Creating;

    /// <summary>
    /// JSON mapping of CheckpointId to checkpoint result.
    /// Format: { "1": {"Completed": true, "Score": 100, "VerifiedAt": "..."}, ... }
    /// </summary>
    public string CheckpointResults { get; set; } = "{}";

    /// <summary>
    /// JSON list of shell command log entries.
    /// Format: [{"Timestamp":"...", "Command":"...", "Output":"..."}, ...]
    /// </summary>
    public string ShellLog { get; set; } = "[]";

    /// <summary>
    /// Number of times the environment has been reset
    /// </summary>
    public int ResetCount { get; set; }

    /// <summary>
    /// JSON of current environment access details.
    /// Format: {"GuacamoleConnectionId":"...", "GuacamoleToken":"...", "SshHost":"...", ...}
    /// </summary>
    public string? AccessDetails { get; set; }

    /// <summary>
    /// Registered time slot ID
    /// </summary>
    [Required]
    public int TimeSlotId { get; set; }

    /// <summary>
    /// Instance creation time
    /// </summary>
    [Required]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Instance end time (null if still active)
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Concurrency token
    /// </summary>
    [Timestamp]
    public uint ConcurrencyToken { get; set; }

    #region Db Relationship

    /// <summary>
    /// Parent IR challenge
    /// </summary>
    [ForeignKey(nameof(ChallengeId))]
    public GameChallenge? Challenge { get; set; }

    /// <summary>
    /// User who owns this instance
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public UserInfo? User { get; set; }

    /// <summary>
    /// Registered time slot
    /// </summary>
    [ForeignKey(nameof(TimeSlotId))]
    public TimeSlot? TimeSlot { get; set; }

    #endregion
}
