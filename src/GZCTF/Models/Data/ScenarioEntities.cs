using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

/// <summary>
/// Status of a scenario instance
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScenarioInstanceStatus>))]
public enum ScenarioInstanceStatus : byte
{
    /// <summary>
    /// The scenario instance is active and the player is progressing through stages
    /// </summary>
    Active = 0,

    /// <summary>
    /// The scenario instance is paused (e.g., time slot expired, admin action)
    /// </summary>
    Paused = 1,

    /// <summary>
    /// All stages have been completed
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The scenario instance has expired
    /// </summary>
    Expired = 3
}

/// <summary>
/// Status of an individual stage within a scenario instance
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StageStatus>))]
public enum StageStatus : byte
{
    /// <summary>
    /// Stage is locked and not yet accessible
    /// </summary>
    Locked = 0,

    /// <summary>
    /// Stage is unlocked and ready for the player
    /// </summary>
    Unlocked = 1,

    /// <summary>
    /// Stage is currently in progress
    /// </summary>
    InProgress = 2,

    /// <summary>
    /// Stage has been completed (flag submitted correctly)
    /// </summary>
    Completed = 3,

    /// <summary>
    /// Stage has been failed
    /// </summary>
    Failed = 4
}

/// <summary>
/// A stage within a multi-stage attack chain scenario.
/// Each stage represents one step in the attack narrative with its own flag and environment configuration.
/// </summary>
[Index(nameof(ScenarioId))]
[Index(nameof(ScenarioId), nameof(OrderIndex))]
public class Stage
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Parent scenario challenge ID (FK to GameChallenge)
    /// </summary>
    [Required]
    public int ScenarioId { get; set; }

    /// <summary>
    /// Order of this stage in the attack chain (0-based)
    /// </summary>
    [Required]
    public int OrderIndex { get; set; }

    /// <summary>
    /// Stage title
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Description of the skill or technique demonstrated in this stage
    /// </summary>
    [MaxLength(1024)]
    public string? SkillDescription { get; set; }

    /// <summary>
    /// SHA256 hash of the stage flag
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string FlagHash { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized list of network rules for this stage.
    /// Each rule specifies source, destination, protocol, and action.
    /// </summary>
    [MaxLength(2048)]
    public string? NetworkRules { get; set; }

    /// <summary>
    /// JSON-serialized list of prerequisite stage IDs that must be completed
    /// before this stage can be unlocked.
    /// </summary>
    [MaxLength(512)]
    public string? PrerequisiteStageIds { get; set; }

    /// <summary>
    /// JSON-serialized list of image template IDs used to provision the environment for this stage.
    /// </summary>
    [MaxLength(512)]
    public string? EnvironmentImageIds { get; set; }

    /// <summary>
    /// Set flag hash from plaintext flag using SHA256
    /// </summary>
    internal void SetFlag(string plainFlag)
    {
        FlagHash = plainFlag.ToSHA256String();
    }

    /// <summary>
    /// Verify a submitted flag against the stored hash
    /// </summary>
    internal bool VerifyFlag(string submittedFlag)
    {
        if (string.IsNullOrEmpty(FlagHash))
            return false;
        return FlagHash == submittedFlag.ToSHA256String();
    }

    #region Db Relationship

    /// <summary>
    /// Parent scenario challenge
    /// </summary>
    [ForeignKey(nameof(ScenarioId))]
    public GameChallenge? Scenario { get; set; }

    #endregion
}

/// <summary>
/// A timeline entry recording a state change in a scenario instance
/// </summary>
public class ScenarioTimelineEntry
{
    /// <summary>
    /// Timestamp of the event
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Stage ID involved (0 for instance-level events)
    /// </summary>
    public int StageId { get; set; }

    /// <summary>
    /// Event type description
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Additional event details
    /// </summary>
    public string? Details { get; set; }
}

/// <summary>
/// A player's instance of a scenario challenge.
/// Tracks progress through the multi-stage attack chain.
/// </summary>
[Index(nameof(ScenarioId))]
[Index(nameof(UserId))]
[Index(nameof(TimeSlotId))]
public class ScenarioInstance
{
    [Key]
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// Parent scenario challenge ID (FK to GameChallenge)
    /// </summary>
    [Required]
    public int ScenarioId { get; set; }

    /// <summary>
    /// User who owns this instance
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// ID of the currently active stage
    /// </summary>
    public int CurrentStageId { get; set; }

    /// <summary>
    /// Overall status of the scenario instance
    /// </summary>
    [Required]
    public ScenarioInstanceStatus Status { get; set; } = ScenarioInstanceStatus.Active;

    /// <summary>
    /// JSON-serialized dictionary mapping StageId to StageStatus
    /// </summary>
    [MaxLength(4096)]
    public string StageStatuses { get; set; } = "{}";

    /// <summary>
    /// JSON-serialized list of timeline entries recording progress
    /// </summary>
    [MaxLength(8192)]
    public string StageTimeline { get; set; } = "[]";

    /// <summary>
    /// When this instance was created
    /// </summary>
    [Required]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Time slot ID this instance belongs to
    /// </summary>
    [Required]
    public int TimeSlotId { get; set; }

    /// <summary>
    /// Concurrency token
    /// </summary>
    [Timestamp]
    public uint ConcurrencyToken { get; set; }

    #region Db Relationship

    /// <summary>
    /// Parent scenario challenge
    /// </summary>
    [ForeignKey(nameof(ScenarioId))]
    public GameChallenge? Scenario { get; set; }

    /// <summary>
    /// Owner user
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public UserInfo? User { get; set; }

    /// <summary>
    /// Associated time slot
    /// </summary>
    [ForeignKey(nameof(TimeSlotId))]
    public TimeSlot? TimeSlot { get; set; }

    #endregion
}
