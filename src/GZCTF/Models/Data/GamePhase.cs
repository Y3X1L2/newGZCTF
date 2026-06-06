using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(GameId))]
public class GamePhase
{
    [Key] public int Id { get; set; }
    public int GameId { get; set; }
    [Required, MaxLength(256)] public string Name { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool CTFEnabled { get; set; } = true;
    [MaxLength(2048)] public string? SecurityPolicy { get; set; }

    [ForeignKey(nameof(GameId))]
    public Game? Game { get; set; }
}
