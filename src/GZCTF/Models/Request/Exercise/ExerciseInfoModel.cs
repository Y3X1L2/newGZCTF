namespace GZCTF.Models.Request.Exercise;

/// <summary>
/// Basic exercise information
/// </summary>
public class ExerciseInfoModel
{
    /// <summary>
    /// Exercise ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Exercise title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Difficulty of the exercise, used for tags, sorting, etc.
    /// </summary>
    public Difficulty Difficulty { get; set; }

    /// <summary>
    /// Exercise category
    /// </summary>
    public ChallengeCategory Category { get; set; }

    /// <summary>
    /// Exercise challenge type.
    /// </summary>
    public ChallengeType Type { get; set; }

    /// <summary>
    /// Whether the exercise is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Additional tags for the exercise
    /// </summary>
    public List<string>? Tags { get; set; } = new();

    /// <summary>
    /// Exercise points
    /// </summary>
    public bool Credit { get; set; }

    public ExercisePoolSource PoolSource { get; set; }

    /// <summary>
    /// Number of people who solved the exercise
    /// </summary>
    public int AcceptedCount { get; set; }

    /// <summary>
    /// Number of submissions
    /// </summary>
    public int SubmissionCount { get; set; }

    /// <summary>
    /// Whether the current user completed this exercise.
    /// </summary>
    public bool Solved { get; set; }

    /// <summary>
    /// Accepted submissions made by the current user.
    /// </summary>
    public int UserAcceptedCount { get; set; }

    /// <summary>
    /// Total submissions made by the current user.
    /// </summary>
    public int UserSubmissionCount { get; set; }
}
