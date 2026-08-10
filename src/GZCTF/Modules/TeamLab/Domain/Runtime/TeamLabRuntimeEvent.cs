using System.ComponentModel.DataAnnotations;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public class TeamLabEvent
{
    [Key] public int Id { get; set; }
    public int? RuntimeId { get; set; }
    public Guid? ControlScopeId { get; set; }
    public int Generation { get; set; } = 1;
    [MaxLength(64)] public string Stage { get; set; } = string.Empty;
    public TeamLabEventLevel Level { get; set; } = TeamLabEventLevel.Info;
    [MaxLength(256)] public string Message { get; set; } = string.Empty;
    [MaxLength(128)] public string? ObjectType { get; set; }
    [MaxLength(128)] public string? ObjectId { get; set; }
    [MaxLength(1024)] public string? Detail { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(64)] public string? ResourceType { get; set; }
    public Guid? ResourcePublicId { get; set; }
    public int ResourceVersion { get; set; }
    public Guid? OperationId { get; set; }
    [MaxLength(512)] public string? ResourceUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime? Runtime { get; set; }
}
