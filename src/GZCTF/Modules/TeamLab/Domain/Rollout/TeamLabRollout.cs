using System.ComponentModel.DataAnnotations;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabRolloutStatus : byte
{
    Draft = 0,
    Preparing = 1,
    RollingOut = 2,
    Ready = 3,
    Draining = 4,
    Completed = 5,
    Blocked = 6,
    Failed = 7
}

public enum TeamLabRolloutTargetStatus : byte
{
    Pending = 0,
    Provisioning = 1,
    Ready = 2,
    AccessOpen = 3,
    Failed = 4,
    Draining = 5,
    CleanupPending = 6,
    Destroyed = 7
}

public sealed class TeamLabRollout
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public Guid ReleaseId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    [MaxLength(64)] public string AdapterKind { get; set; } = string.Empty;
    [MaxLength(256)] public string ExternalReference { get; set; } = string.Empty;
    public TeamLabRolloutStatus Status { get; set; } = TeamLabRolloutStatus.Draft;
    public bool PreparationRequested { get; set; }
    public bool DesiredAccessOpen { get; set; }
    public bool DrainRequested { get; set; }
    public int Revision { get; set; }
    [MaxLength(2048)] public string? LastError { get; set; }
    public DateTimeOffset? PreparedAt { get; set; }
    public DateTimeOffset? AccessOpenedAt { get; set; }
    public DateTimeOffset? DrainingAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabTopologyRelease Release { get; set; } = null!;
    public List<TeamLabRolloutTarget> Targets { get; set; } = [];
}

public sealed class TeamLabRolloutTarget
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RolloutId { get; set; }
    [MaxLength(256)] public string ExternalSubject { get; set; } = string.Empty;
    [MaxLength(256)] public string DisplayName { get; set; } = string.Empty;
    public int? RuntimeId { get; set; }
    public TeamLabRolloutTargetStatus Status { get; set; } = TeamLabRolloutTargetStatus.Pending;
    public Guid? LastOperationId { get; set; }
    [MaxLength(2048)] public string? LastError { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? DestroyedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRollout Rollout { get; set; } = null!;
    public TeamLabRuntime? Runtime { get; set; }
}
