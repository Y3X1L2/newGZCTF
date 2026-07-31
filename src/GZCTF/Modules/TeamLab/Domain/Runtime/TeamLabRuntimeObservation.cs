using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public enum TeamLabObservationPointKind : byte
{
    NetworkBridge = 0,
    RouterFragment = 1,
    FabricUplink = 2,
    WorkloadEndpoint = 3
}

public sealed class TeamLabObservationPoint
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public Guid WorkerNodeId { get; set; }
    public int? ShardId { get; set; }
    public int? NetworkId { get; set; }
    public int? InfrastructureFragmentId { get; set; }
    public int? AssetId { get; set; }
    public TeamLabObservationPointKind Kind { get; set; }
    [MaxLength(63)] public string TopologyKey { get; set; } = string.Empty;
    [MaxLength(128)] public string InterfaceToken { get; set; } = string.Empty;
    [MaxLength(128)] public string? DesiredStateDigest { get; set; }
    public bool Enabled { get; set; } = true;
    public long LastSequence { get; set; }
    public long DroppedPackets { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeShard? Shard { get; set; }
    public TeamLabRuntimeNetwork? Network { get; set; }
    public TeamLabRuntimeInfrastructureFragment? InfrastructureFragment { get; set; }
    public TeamLabRuntimeAsset? Asset { get; set; }
    public WorkerNode WorkerNode { get; set; } = null!;
}
