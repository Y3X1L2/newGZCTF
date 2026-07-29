using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(ExerciseChallengeId), nameof(UserId))]
[Index(nameof(UserId), nameof(SubmittedAt))]
public class ExerciseSubmission
{
    [Key]
    public long Id { get; set; }

    public int ExerciseChallengeId { get; set; }

    public ExerciseChallenge ExerciseChallenge { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public AnswerResult Status { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(128)]
    public string SubmittedAnswerHash { get; set; } = string.Empty;

    public int? FlagId { get; set; }

    public FlagContext? Flag { get; set; }

    [MaxLength(64)]
    public string IpAddress { get; set; } = string.Empty;
}
