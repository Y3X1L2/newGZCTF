using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(GameId), nameof(TeamId), nameof(ObjectiveId))]
public class PenetrationSubmission
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }

    public int TeamId { get; set; }

    public int ParticipationId { get; set; }

    public Guid UserId { get; set; }

    public int ObjectiveId { get; set; }

    [MaxLength(Limits.MaxFlagLength)]
    public string Answer { get; set; } = string.Empty;

    public AnswerResult Status { get; set; } = AnswerResult.FlagSubmitted;

    public int Score { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    public Game Game { get; set; } = null!;

    public Team Team { get; set; } = null!;

    public UserInfo User { get; set; } = null!;

    public Participation Participation { get; set; } = null!;

    public GZCTF.Modules.Penetration.Domain.PenetrationObjective Objective { get; set; } = null!;
}

[Index(nameof(RuntimeId))]
public class PenetrationResetRecord
{
    [Key]
    public int Id { get; set; }

    public int RuntimeId { get; set; }

    public Guid? UserId { get; set; }

    public bool ByAdmin { get; set; }

    public DateTimeOffset ResetAt { get; set; } = DateTimeOffset.UtcNow;

    public TeamLabRuntime Runtime { get; set; } = null!;

    public UserInfo? User { get; set; }
}
