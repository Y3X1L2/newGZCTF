using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

/// <summary>
/// Represents the first successful solve of a challenge for a specific participation.
/// This table acts as the immutable fact source for scoreboard and scoring related logic.
/// </summary>
[Index(nameof(SubmissionId), IsUnique = true)]
[PrimaryKey(nameof(ParticipationId), nameof(ChallengeId), nameof(FlagId))]
public class FirstSolve
{
    /// <summary>
    /// Participation ID.
    /// </summary>
    [Required]
    public int ParticipationId { get; set; }

    /// <summary>
    /// Challenge ID.
    /// </summary>
    [Required]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Submission ID that produced this solve.
    /// </summary>
    [Required]
    public int SubmissionId { get; set; }

    /// <summary>
    /// Flag ID.
    /// </summary>
    [Required]
    public int FlagId { get; set; }

    /// <summary>
    /// Flag context.
    /// </summary>
    public FlagContext? FlagContext { get; set; }

    #region Navigation Properties

    public Participation Participation { get; set; } = null!;

    public GameChallenge Challenge { get; set; } = null!;

    public Submission Submission { get; set; } = null!;

    #endregion
}
