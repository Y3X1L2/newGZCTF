using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Data;

public class ExerciseChallenge : Challenge
{
    /// <summary>Account that originally created this public exercise.</summary>
    public Guid? CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public UserInfo? CreatedBy { get; set; }

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

    /// <summary>Source classification shown in the public exercise pool.</summary>
    public ExercisePoolSource PoolSource { get; set; } = ExercisePoolSource.Exercise;

    /// <summary>Original game ID when this entry was collected from a game.</summary>
    public int? SourceGameId { get; set; }

    /// <summary>Original course ID when this entry was collected from training.</summary>
    public int? SourceTrainingCourseId { get; set; }

    /// <summary>Original source challenge ID used for idempotent collection.</summary>
    public int? SourceChallengeId { get; set; }

    /// <summary>Original AWDP service ID when this entry was collected from an AWDP game.</summary>
    public int? SourceAwdpServiceId { get; set; }

    /// <summary>Lowest role allowed to browse and run this pool entry.</summary>
    public Role MinimumVisibleRole { get; set; } = Role.Student;

    [JsonIgnore]
    public TrainingCourse? TrainingCourse { get; set; }

    #region Db Relationship

    /// <summary>
    /// Dependent exercise challenges
    /// </summary>
    public List<ExerciseChallenge> Dependencies { get; set; } = [];

    #endregion
}
