using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Game;

public class AwdServiceCreateModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string ImageName { get; set; } = string.Empty;

    public int ExposePort { get; set; } = 80;
    public string? CheckerScript { get; set; }
    public string? CheckerEntrypoint { get; set; } = "python checker.py";
    public int AttackPoints { get; set; } = 50;
    public int SlaPoints { get; set; } = 20;
    public int MaxAttackPerRound { get; set; } = 3;
    public int RoundDurationMinutes { get; set; } = 5;
    public int TotalRounds { get; set; } = 20;
}

public class AwdServiceUpdateModel : AwdServiceCreateModel
{
}

public class AwdServiceViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public int ExposePort { get; set; }
    public int AttackPoints { get; set; }
    public int SlaPoints { get; set; }
    public int RoundDurationMinutes { get; set; }
    public int TotalRounds { get; set; }
}

public class AwdSubmitModel
{
    [Required]
    public string Flag { get; set; } = string.Empty;

    [Required]
    public int TargetTeamId { get; set; }

    [Required]
    public int ServiceId { get; set; }
}

public class AwdGameStatusModel
{
    public int GameId { get; set; }
    public int CurrentRound { get; set; }
    public DateTimeOffset RoundStartTime { get; set; }
    public int RoundDurationMinutes { get; set; }
    public AwdRoundStatus Status { get; set; }
}

public class AwdServiceStatusModel
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public List<TeamServiceStatus> TeamStatuses { get; set; } = [];
}

public class TeamServiceStatus
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? Port { get; set; }
    public CheckerStatus? LastCheckerStatus { get; set; }
    public bool IsRunning { get; set; }
}

public class AwdScoreboardItem
{
    public int Rank { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int CtfScore { get; set; }
    public int AwdScore { get; set; }
    public int TotalScore => CtfScore + AwdScore;
    public int AttackScore { get; set; }
    public int SlaScore { get; set; }
    public int DefenseLost { get; set; }
}

public class AwdAttackLogItem
{
    public DateTimeOffset Time { get; set; }
    public string AttackerTeam { get; set; } = string.Empty;
    public string VictimTeam { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public int Points { get; set; }
}
