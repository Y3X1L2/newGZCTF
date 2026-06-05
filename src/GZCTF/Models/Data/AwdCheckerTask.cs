using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public enum CheckerStatus
{
    OK,
    Mumble,
    Down,
    Corrupt
}

public class AwdCheckerTask
{
    [Key]
    public int Id { get; set; }

    public int RoundId { get; set; }
    public AwdRound Round { get; set; } = null!;

    public int ServiceId { get; set; }
    public AwdService Service { get; set; } = null!;

    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public CheckerStatus Status { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
}
