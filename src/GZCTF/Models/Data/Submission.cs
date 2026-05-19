using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(UserId))]
[Index(nameof(TeamId), nameof(ChallengeId), nameof(GameId))]
public class Submission
{
    [Key]
    [JsonIgnore]
    public int Id { get; set; }

    /// <summary>
    /// Submitted answer string
    /// </summary>
    [MaxLength(Limits.MaxFlagLength)]
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Status of the submitted answer
    /// </summary>
    public AnswerResult Status { get; set; } = AnswerResult.Accepted;

    /// <summary>
    /// Time the answer was submitted
    /// </summary>
    [JsonPropertyName("time")]
    public DateTimeOffset SubmitTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// User who submitted
    /// </summary>
    [JsonPropertyName("user")]
    public string UserName => User?.UserName ?? string.Empty;

    /// <summary>
    /// Team that submitted
    /// </summary>
    [JsonPropertyName("team")]
    public string TeamName => Team?.Name ?? string.Empty;

    /// <summary>
    /// Challenge that was submitted
    /// </summary>
    [JsonPropertyName("challenge")]
    public string ChallengeName => GameChallenge?.Title ?? string.Empty;

    /// <summary>
    /// Type of submission (Flag, Writeup, IP, Credential, Custom)
    /// </summary>
    public ScoringSubmissionType SubmissionType { get; set; } = ScoringSubmissionType.Flag;

    /// <summary>
    /// JSON content of the submission (used for Writeup, IP, Credential, Custom types)
    /// </summary>
    [MaxLength(4096)]
    public string? Content { get; set; }

    /// <summary>
    /// Admin reviewer ID for ManualReview submissions
    /// </summary>
    [JsonIgnore]
    public Guid? ReviewedById { get; set; }

    /// <summary>
    /// Reviewer feedback/comment
    /// </summary>
    [MaxLength(1024)]
    public string? ReviewComment { get; set; }

    /// <summary>
    /// Attempt number for this submission type (starts at 1)
    /// </summary>
    public int AttemptNumber { get; set; } = 1;

    /// <summary>
    /// Score awarded for this submission (set by scoring engine or manual review)
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Concurrency token
    /// </summary>
    [Timestamp]
    public uint ConcurrencyToken { get; set; }

    #region Db Relationship

    /// <summary>
    /// User database ID
    /// </summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    /// <summary>
    /// User
    /// </summary>
    [JsonIgnore]
    public UserInfo? User { get; set; }

    /// <summary>
    /// Participating team ID
    /// </summary>
    [JsonIgnore]
    public int TeamId { get; set; }

    /// <summary>
    /// Team
    /// </summary>
    [JsonIgnore]
    public Team? Team { get; set; }

    /// <summary>
    /// Participation ID
    /// </summary>
    [JsonIgnore]
    public int ParticipationId { get; set; }

    /// <summary>
    /// Team participation object
    /// </summary>
    [JsonIgnore]
    public Participation Participation { get; set; } = null!;

    /// <summary>
    /// Game ID
    /// </summary>
    [JsonIgnore]
    public int GameId { get; set; }

    /// <summary>
    /// Game
    /// </summary>
    [JsonIgnore]
    public Game? Game { get; set; } = null!;

    /// <summary>
    /// Challenge database ID
    /// </summary>
    [JsonIgnore]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Challenge
    /// </summary>
    [JsonIgnore]
    public GameChallenge? GameChallenge { get; set; }

    /// <summary>
    /// Admin reviewer for ManualReview submissions
    /// </summary>
    [JsonIgnore]
    public UserInfo? ReviewedBy { get; set; }

    #endregion Db Relationship
}
