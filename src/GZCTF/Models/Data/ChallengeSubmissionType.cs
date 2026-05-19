using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GZCTF.Models.Data;

/// <summary>
/// Admin-configured submission type whitelist per challenge.
/// CRITICAL-6 FIX: MaxAttempts and ScoreDecay live in ScoringRule, not here.
/// This entity only defines UI presentation (label, file requirements, order).
/// </summary>
public class ChallengeSubmissionType
{
    [Key] public int Id { get; set; }

    [Required] public int ChallengeId { get; set; }

    [Required]
    public ScoringSubmissionType SubmissionType { get; set; } = ScoringSubmissionType.Flag;

    /// <summary>Display order in the submission form</summary>
    public int OrderIndex { get; set; }

    /// <summary>Display label shown to players (e.g. "Flag", "Writeup Report", "Virus Sample")</summary>
    [MaxLength(128)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Whether file upload is required for this submission type</summary>
    public bool RequireFile { get; set; }

    /// <summary>Accepted file extensions when RequireFile is true (e.g. ".exe,.dll,.pcap")</summary>
    [MaxLength(256)]
    public string? AcceptedFileExtensions { get; set; }

    /// <summary>Maximum file size in MB</summary>
    public int MaxFileSize { get; set; } = 10;

    /// <summary>Whether this submission type is currently accepting submissions</summary>
    public bool IsActive { get; set; } = true;

    #region Db Relationship
    [ForeignKey(nameof(ChallengeId))]
    public GameChallenge? Challenge { get; set; }
    #endregion
}
