using System.ComponentModel.DataAnnotations;

namespace GZCTF.Modules.TeamLab.Domain;

/// <summary>
/// Stable external ownership boundary for TeamLab resources. It intentionally
/// carries no user directory, billing, or scheduling concerns.
/// </summary>
public sealed class TeamLabControlScope
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    [MaxLength(96)] public string Key { get; set; } = string.Empty;
    [MaxLength(128)] public string DisplayName { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
