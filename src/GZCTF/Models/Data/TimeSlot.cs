using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GZCTF.Models.Data;

public class TimeSlot
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Parent scenario challenge ID
    /// </summary>
    [Required]
    public int ScenarioId { get; set; }

    /// <summary>
    /// Start time of the time slot
    /// </summary>
    [Required]
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// End time of the time slot
    /// </summary>
    [Required]
    public DateTimeOffset EndTime { get; set; }

    /// <summary>
    /// Maximum number of participants that can register for this slot
    /// </summary>
    [Required]
    public int MaxParticipants { get; set; } = 20;

    /// <summary>
    /// Current number of registered participants
    /// </summary>
    public int CurrentParticipants { get; set; }

    #region Db Relationship

    /// <summary>
    /// Parent scenario challenge
    /// </summary>
    [ForeignKey(nameof(ScenarioId))]
    public GameChallenge? Scenario { get; set; }

    #endregion
}
