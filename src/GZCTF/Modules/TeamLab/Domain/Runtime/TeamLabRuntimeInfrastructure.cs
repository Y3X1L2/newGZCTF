using System.ComponentModel.DataAnnotations;
using System.Net;
using GZCTF.Models.Data;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public sealed class TeamLabRuntimeInfrastructure
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    [MaxLength(63)] public string TopologyKey { get; set; } = string.Empty;
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    public TeamLabInfrastructureKind Kind { get; set; }
    [MaxLength(63)] public string? NetworkKey { get; set; }
    public string InterfaceSummaryJson { get; set; } = "[]";
    public string ConnectionSummaryJson { get; set; } = "[]";
    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;
    [MaxLength(128)] public string? DesiredStateDigest { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public List<TeamLabRuntimeInfrastructureFragment> Fragments { get; set; } = [];
}

public sealed class TeamLabRuntimeInfrastructureFragment
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int InfrastructureId { get; set; }
    public int ShardId { get; set; }
    public Guid WorkerNodeId { get; set; }
    [MaxLength(128)] public string FragmentKey { get; set; } = string.Empty;
    public string InterfaceSummaryJson { get; set; } = "[]";
    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;
    [MaxLength(256)] public string? NativeResourceId { get; set; }
    [MaxLength(128)] public string? DesiredStateDigest { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public TeamLabRuntimeInfrastructure Infrastructure { get; set; } = null!;
    public TeamLabRuntimeShard Shard { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
}

public sealed class TeamLabFabricLinkLease
{
    [Key] public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public int ShardId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public IPNetwork AllocatedCidr { get; set; }
    [MaxLength(64)] public string HubAddress { get; set; } = string.Empty;
    [MaxLength(64)] public string NodeAddress { get; set; } = string.Empty;
    public DateTimeOffset AllocatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeShard Shard { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
}

public enum TeamLabDependencyStateStatus : byte
{
    Pending = 0,
    Satisfied = 1,
    Failed = 2
}

public sealed class TeamLabRuntimeDependencyState
{
    [Key] public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    [MaxLength(63)] public string AssetKey { get; set; } = string.Empty;
    [MaxLength(63)] public string DependsOnKey { get; set; } = string.Empty;
    public TeamLabDependencyCondition Condition { get; set; }
    public TeamLabDependencyStateStatus Status { get; set; } = TeamLabDependencyStateStatus.Pending;
    public DateTimeOffset? SatisfiedAt { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
}
