using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public enum AwdRoundStatus
{
    Preparing,
    Running,
    Finished
}

public class AwdRound
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public int RoundNumber { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public AwdRoundStatus Status { get; set; } = AwdRoundStatus.Preparing;

    public List<AwdFlag> Flags { get; set; } = [];
    public List<AwdCheckerTask> CheckerTasks { get; set; } = [];
}
