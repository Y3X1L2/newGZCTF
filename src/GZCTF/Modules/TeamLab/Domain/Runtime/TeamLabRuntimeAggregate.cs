using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public class TeamLabRuntime
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public Guid? ControlScopeId { get; set; }
    public Guid TopologyReleaseId { get; set; }
    public Guid? CreatedById { get; set; }
    public int Generation { get; set; } = 1;
    [MaxLength(256)] public string? ExternalReference { get; set; }
    [MaxLength(128)] public string? CreationIdempotencyKey { get; set; }
    [MaxLength(128)] public string CreateRequestHash { get; set; } = string.Empty;
    public int? EntryShardId { get; set; }
    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;
    public bool IsOpenToPlayers { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<TeamLabRuntimeShard> Shards { get; set; } = [];
    public List<TeamLabRuntimeNetwork> Networks { get; set; } = [];
    public List<TeamLabRuntimeAsset> Assets { get; set; } = [];
    public List<TeamLabRuntimeInfrastructure> Infrastructure { get; set; } = [];
    public List<TeamLabExecutionPlanSnapshot> ExecutionPlanSnapshots { get; set; } = [];
    public List<TeamLabRuntimeDependencyState> DependencyStates { get; set; } = [];
    public List<TeamLabObservationPoint> ObservationPoints { get; set; } = [];
    public List<TeamLabObservationCursor> ObservationCursors { get; set; } = [];
    public List<TeamLabFabricLinkLease> FabricLinkLeases { get; set; } = [];
    public List<TeamLabVpnPeerRuntime> VpnPeers { get; set; } = [];
    public List<TeamLabAccessGrant> AccessGrants { get; set; } = [];
    public List<TeamLabRuntimeSecretEnvelope> SecretEnvelopes { get; set; } = [];
    public TeamLabPublicUdpMapping? PublicUdpMapping { get; set; }
    public List<TeamLabEvent> Events { get; set; } = [];
    public List<TeamLabTrafficFlow> TrafficFlows { get; set; } = [];
    public List<TeamLabTrafficObservation> TrafficObservations { get; set; } = [];
    public List<TeamLabTrafficPath> TrafficPaths { get; set; } = [];
    public List<TeamLabTrafficCorrelationCursor> TrafficCorrelationCursors { get; set; } = [];
    public List<TeamLabTrafficCaptureJob> TrafficCaptureJobs { get; set; } = [];
    public TeamLabControlScope? ControlScope { get; set; }
}

/// <summary>
/// Immutable V2 plan accepted for one runtime generation and node shard. This is the cleanup
/// authority after deployment; it deliberately does not contain runtime secret overlays.
/// </summary>
public sealed class TeamLabExecutionPlanSnapshot
{
    [Key] public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public int ShardId { get; set; }
    public Guid WorkerNodeId { get; set; }
    [MaxLength(64)] public string PlanDigest { get; set; } = string.Empty;
    [MaxLength(32)] public string SchemaVersion { get; set; } = "v2";
    public string PlanJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeShard Shard { get; set; } = null!;
}

public class TeamLabRuntimeShard
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public Guid WorkerNodeId { get; set; }
    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;
    public int RouteVersion { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
    public List<TeamLabRuntimeNetwork> Networks { get; set; } = [];
    public List<TeamLabRuntimeAsset> Assets { get; set; } = [];
}

public class TeamLabRuntimeNetwork
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public long? NetworkLeaseId { get; set; }
    public int? ShardId { get; set; }
    public Guid? WorkerNodeId { get; set; }
    [MaxLength(256)] public string PlacementGroupKey { get; set; } = string.Empty;
    public bool IsEntry { get; set; }
    [MaxLength(64)] public string TopologyKey { get; set; } = string.Empty;
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(64)] public string Cidr { get; set; } = string.Empty;
    [MaxLength(64)] public string GatewayIp { get; set; } = string.Empty;
    [MaxLength(128)] public string BridgeName { get; set; } = string.Empty;
    public long FlowCursor { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeShard? Shard { get; set; }
    public WorkerNode? WorkerNode { get; set; }
    public TeamLabNetworkLease? NetworkLease { get; set; }
}

public class TeamLabRuntimeAsset
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public int? ShardId { get; set; }
    public Guid? WorkerNodeId { get; set; }
    [MaxLength(256)] public string PlacementGroupKey { get; set; } = string.Empty;
    public TeamLabResourceKind Kind { get; set; }
    [MaxLength(64)] public string TopologyKey { get; set; } = string.Empty;
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string? RuntimeResourceId { get; set; }
    [MaxLength(128)] public string? NativeIdentity { get; set; }
    public int? SourceTemplateId { get; set; }
    [MaxLength(512)] public string? Image { get; set; }
    [MaxLength(64)] public string? NetworkKey { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(64)] public string? MacAddress { get; set; }
    [MaxLength(4096)] public string InterfaceSummaryJson { get; set; } = "[]";
    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;
    public TeamLabAssetExecutionStage ExecutionStage { get; set; } = TeamLabAssetExecutionStage.Pending;
    public Guid? AgentOperationId { get; set; }
    public long AgentSignalSequence { get; set; }
    public TeamLabEndpointObservationMode EndpointObservation { get; set; }
    [MaxLength(128)] public string? ImageDigest { get; set; }
    public DateTimeOffset? ExecutionUpdatedAt { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeShard? Shard { get; set; }
    public WorkerNode? WorkerNode { get; set; }
}
