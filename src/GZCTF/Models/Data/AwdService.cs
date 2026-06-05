using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public class AwdService
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string ImageName { get; set; } = string.Empty;

    public int ExposePort { get; set; }

    public string? CheckerScript { get; set; }
    public string? CheckerEntrypoint { get; set; } = "python checker.py";

    public int OriginalScore { get; set; } = 1000;
    public int AttackPoints { get; set; } = 50;
    public int SlaPoints { get; set; } = 20;
    public int MaxAttackPerRound { get; set; } = 3;

    public int RoundDurationMinutes { get; set; } = 5;
    public int TotalRounds { get; set; } = 20;

    public List<AwdServiceInstance> Instances { get; set; } = [];
    public List<AwdRound> Rounds { get; set; } = [];
}
