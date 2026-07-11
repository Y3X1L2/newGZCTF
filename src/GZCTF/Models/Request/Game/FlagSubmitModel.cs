using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Game;

/// <summary>
/// Flag submission
/// </summary>
public class FlagSubmitModel
{
    /// <summary>
    /// Flag content
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_FlagRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Flag { get; set; } = string.Empty;

    /// <summary>
    /// Specific Flag ID being submitted against (multi-flag challenges)
    /// </summary>
    public int? FlagId { get; set; }
}

/// <summary>
/// Flag submission result
/// </summary>
public class FlagSubmitResultModel
{
    /// <summary>
    /// Submission ID
    /// </summary>
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Answer verification result
    /// </summary>
    [Required]
    public AnswerResult Status { get; set; }

    /// <summary>
    /// Blood rank awarded by this submission
    /// </summary>
    [Required]
    public SubmissionType BloodType { get; set; }
}
