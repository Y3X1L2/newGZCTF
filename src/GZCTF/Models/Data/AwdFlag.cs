using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public class AwdFlag
{
    [Key]
    public int Id { get; set; }

    public int RoundId { get; set; }
    public AwdRound Round { get; set; } = null!;

    public int ServiceId { get; set; }
    public AwdService Service { get; set; } = null!;

    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required]
    public string FlagValue { get; set; } = string.Empty;

    public bool IsSubmitted { get; set; }
    public DateTimeOffset? FirstSubmittedAt { get; set; }
}
