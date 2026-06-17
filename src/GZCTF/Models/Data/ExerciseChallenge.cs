using System.Text.Json.Serialization;

namespace GZCTF.Models.Data;

public class ExerciseChallenge : Challenge
{
    /// <summary>
    /// Credits for the exercise challenge
    /// </summary>
    public bool Credit { get; set; }

    /// <summary>
    /// Difficulty of the exercise challenge, used for tags, sorting, etc.
    /// </summary>
    public Difficulty Difficulty { get; set; }

    /// <summary>
    /// Additional tags for the exercise challenge
    /// </summary>
    public List<string>? Tags { get; set; } = [];

    /// <summary>
    /// Owning training course. Null means global exercise challenge.
    /// </summary>
    public int? TrainingCourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse? TrainingCourse { get; set; }

    #region Db Relationship

    /// <summary>
    /// Dependent exercise challenges
    /// </summary>
    public List<ExerciseChallenge> Dependencies { get; set; } = [];

    #endregion
}
