using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

public enum TeamLabRuntimeStatus : byte
{
    Pending = 0,
    Planning = 1,
    Scheduled = 2,
    Deploying = 3,
    Probing = 4,
    Running = 5,
    Failed = 6,
    CleanupPending = 7,
    Stopped = 8,
    Destroying = 9,
    Destroyed = 10
}

public enum TeamLabResourceKind : byte
{
    Docker = 0,
    Vm = 1,
    RouterNamespace = 2,
    DhcpDnsService = 3,
    WireGuard = 4,
    PublicUdpMapping = 5
}

public enum TeamLabEventLevel : byte
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public enum TeamLabTrafficCaptureStatus : byte
{
    Pending = 0,
    Running = 1,
    Stopping = 2,
    Completed = 3,
    Failed = 4,
    Expired = 5
}

public enum TeamLabAccessGrantType : byte
{
    WireGuard = 0
}

public class TeamLabRuntime
{
    [Key] public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.CreateVersion7();

    public Guid TopologyReleaseId { get; set; }

    public Guid? CreatedById { get; set; }

    public int Generation { get; set; } = 1;

    [MaxLength(256)] public string? ExternalReference { get; set; }

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

    public List<TeamLabVpnPeerRuntime> VpnPeers { get; set; } = [];

    public List<TeamLabAccessGrant> AccessGrants { get; set; } = [];

    public List<TeamLabRuntimeSecretEnvelope> SecretEnvelopes { get; set; } = [];

    public TeamLabPublicUdpMapping? PublicUdpMapping { get; set; }

    public List<TeamLabEvent> Events { get; set; } = [];

    public List<TeamLabTrafficFlow> TrafficFlows { get; set; } = [];

    public List<TeamLabTrafficCaptureJob> TrafficCaptureJobs { get; set; } = [];
}

[Index(nameof(RuntimeId), nameof(Generation), nameof(WorkerNodeId), IsUnique = true)]
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

[Index(nameof(RuntimeId), nameof(Generation), nameof(TopologyKey), IsUnique = true)]
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

    public GZCTF.Modules.TeamLab.Domain.TeamLabNetworkLease? NetworkLease { get; set; }
}

[Index(nameof(RuntimeId), nameof(Generation), nameof(Kind), nameof(TopologyKey))]
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

    public int? SourceTemplateId { get; set; }

    [MaxLength(512)] public string? Image { get; set; }

    [MaxLength(64)] public string? NetworkKey { get; set; }

    [MaxLength(64)] public string? IpAddress { get; set; }

    [MaxLength(64)] public string? MacAddress { get; set; }

    [MaxLength(4096)] public string InterfaceSummaryJson { get; set; } = "[]";

    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;

    [MaxLength(1024)] public string? LastError { get; set; }

    public TeamLabRuntime Runtime { get; set; } = null!;

    public TeamLabRuntimeShard? Shard { get; set; }

    public WorkerNode? WorkerNode { get; set; }
}

[Index(nameof(RuntimeId), nameof(Revoked))]
public class TeamLabVpnPeerRuntime
{
    [Key] public int Id { get; set; }

    public int RuntimeId { get; set; }

    [MaxLength(64)] public string ClientAddress { get; set; } = string.Empty;

    [MaxLength(256)] public string Endpoint { get; set; } = string.Empty;

    [MaxLength(256)] public string AllowedIPs { get; set; } = string.Empty;

    [MaxLength(64)] public string Dns { get; set; } = string.Empty;

    [MaxLength(128)] public string PublicKey { get; set; } = string.Empty;

    [MaxLength(1024)] public string ProtectedClientPrivateKey { get; set; } = string.Empty;

    [MaxLength(128)] public string ServerPublicKey { get; set; } = string.Empty;

    [MaxLength(1024)] public string ProtectedServerPrivateKey { get; set; } = string.Empty;

    public int ConfigVersion { get; set; } = 1;

    public bool Revoked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TeamLabRuntime Runtime { get; set; } = null!;
}

[Index(nameof(RuntimeId), nameof(Generation), nameof(Revoked))]
public class TeamLabAccessGrant
{
    [Key] public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.CreateVersion7();

    public int RuntimeId { get; set; }

    public int Generation { get; set; } = 1;

    public Guid? ApiOperationId { get; set; }

    public TeamLabAccessGrantType Type { get; set; } = TeamLabAccessGrantType.WireGuard;

    [MaxLength(64)] public string ClientAddress { get; set; } = string.Empty;

    [MaxLength(256)] public string Endpoint { get; set; } = string.Empty;

    [MaxLength(512)] public string AllowedIps { get; set; } = string.Empty;

    [MaxLength(64)] public string Dns { get; set; } = string.Empty;

    [MaxLength(128)] public string PublicKey { get; set; } = string.Empty;

    [MaxLength(1024)] public string ProtectedPrivateKey { get; set; } = string.Empty;

    [MaxLength(128)] public string ServerPublicKey { get; set; } = string.Empty;

    [MaxLength(1024)] public string ProtectedServerPrivateKey { get; set; } = string.Empty;

    [MaxLength(128)] public string DownloadTokenHash { get; set; } = string.Empty;

    [MaxLength(1024)] public string? ProtectedDownloadToken { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public bool Revoked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? ConfigurationConsumedAt { get; set; }

    public TeamLabRuntime Runtime { get; set; } = null!;
}

[Index(nameof(RuntimeId), nameof(Generation), IsUnique = true)]
public class TeamLabRuntimeSecretEnvelope
{
    [Key] public long Id { get; set; }

    public int RuntimeId { get; set; }

    public int Generation { get; set; } = 1;

    public string? ProtectedPayload { get; set; }

    [MaxLength(128)] public string PayloadHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public TeamLabRuntime Runtime { get; set; } = null!;
}

[Index(nameof(RuntimeId), IsUnique = true)]
[Index(nameof(PublicUdpPort), IsUnique = true)]
public class TeamLabPublicUdpMapping
{
    [Key] public int Id { get; set; }

    public int RuntimeId { get; set; }

    public int Generation { get; set; } = 1;

    public int PublicUdpPort { get; set; }

    [MaxLength(64)] public string WorkerTunnelIp { get; set; } = string.Empty;

    public int WorkerWireGuardPort { get; set; }

    public int RuleVersion { get; set; }

    public bool IsSynced { get; set; }

    [MaxLength(1024)] public string? LastSyncError { get; set; }

    public TeamLabRuntime Runtime { get; set; } = null!;
}

[Index(nameof(RuntimeId), nameof(CreatedAt))]
public class TeamLabEvent
{
    [Key] public int Id { get; set; }

    public int RuntimeId { get; set; }

    public int Generation { get; set; } = 1;

    [MaxLength(64)] public string Stage { get; set; } = string.Empty;

    public TeamLabEventLevel Level { get; set; } = TeamLabEventLevel.Info;

    [MaxLength(256)] public string Message { get; set; } = string.Empty;

    [MaxLength(128)] public string? ObjectType { get; set; }

    [MaxLength(128)] public string? ObjectId { get; set; }

    [MaxLength(1024)] public string? Detail { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabTrafficFlow
{
    [Key] public long Id { get; set; }

    public int RuntimeId { get; set; }

    public int Generation { get; set; } = 1;

    public long SourceCursor { get; set; }

    public int? ShardId { get; set; }

    public int? NetworkId { get; set; }

    public Guid? WorkerNodeId { get; set; }

    [MaxLength(64)] public string SourceIp { get; set; } = string.Empty;

    [MaxLength(64)] public string SourcePrefix { get; set; } = string.Empty;

    public int? SourcePort { get; set; }

    [MaxLength(64)] public string DestinationIp { get; set; } = string.Empty;

    [MaxLength(64)] public string DestinationPrefix { get; set; } = string.Empty;

    public int? DestinationPort { get; set; }

    [MaxLength(16)] public string Protocol { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public long Packets { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public byte[] Fingerprint { get; set; } = [];

    public TeamLabRuntime Runtime { get; set; } = null!;

    public TeamLabRuntimeShard? Shard { get; set; }

    public TeamLabRuntimeNetwork? Network { get; set; }

    public WorkerNode? WorkerNode { get; set; }
}

[Index(nameof(RuntimeId), nameof(Status))]
[Index(nameof(ShardId), nameof(Status))]
[Index(nameof(ApiOperationId), Name = "UX_TeamLabCapture_ApiOperation", IsUnique = true)]
public class TeamLabTrafficCaptureJob
{
    [Key] public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.CreateVersion7();

    public int RuntimeId { get; set; }

    public int Generation { get; set; } = 1;

    public Guid? ApiOperationId { get; set; }

    public int? ShardId { get; set; }

    public int? NetworkId { get; set; }

    public Guid? WorkerNodeId { get; set; }

    public TeamLabTrafficCaptureStatus Status { get; set; } = TeamLabTrafficCaptureStatus.Pending;

    [MaxLength(64)] public string Scope { get; set; } = string.Empty;

    [MaxLength(512)] public string? FilePath { get; set; }

    public long MaxBytes { get; set; }

    public int MaxSeconds { get; set; }

    public long CapturedBytes { get; set; }

    [MaxLength(1024)] public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public TeamLabRuntime Runtime { get; set; } = null!;

    public TeamLabRuntimeShard? Shard { get; set; }

    public TeamLabRuntimeNetwork? Network { get; set; }

    public WorkerNode? WorkerNode { get; set; }
}
