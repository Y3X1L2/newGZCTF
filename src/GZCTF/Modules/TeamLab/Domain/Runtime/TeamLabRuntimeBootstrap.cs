using System.ComponentModel.DataAnnotations;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public enum TeamLabBootstrapExecutionStatus : byte
{
    Pending = 0,
    Running = 1,
    Rebooting = 2,
    Succeeded = 3,
    Failed = 4
}

public sealed class TeamLabBootstrapExecution
{
    [Key] public long Id { get; set; }
    public Guid ExecutionId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public int AssetId { get; set; }
    public Guid ProfileId { get; set; }
    public int ProfileVersion { get; set; }
    [MaxLength(63)] public string StepKey { get; set; } = string.Empty;
    public int Attempt { get; private set; } = 1;
    public long BootEpoch { get; set; }
    public TeamLabBootstrapExecutionStatus Status { get; set; } = TeamLabBootstrapExecutionStatus.Pending;
    [MaxLength(128)] public string? InputDigest { get; set; }
    [MaxLength(128)] public string? OutputDigest { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeAsset Asset { get; set; } = null!;
}
