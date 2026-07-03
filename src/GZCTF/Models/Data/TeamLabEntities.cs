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

[Index(nameof(GameId), nameof(TeamId), IsUnique = true)]
[Index(nameof(WorkerNodeId))]
public class TeamLabRuntime
{
    [Key] public int Id { get; set; }

    public int GameId { get; set; }

    public int TeamId { get; set; }

    public int PublishedVersion { get; set; }

    public Guid? WorkerNodeId { get; set; }

    [MaxLength(64)] public string NetworkPrefix { get; set; } = string.Empty;

    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;

    public bool IsOpenToPlayers { get; set; }

    [MaxLength(1024)] public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;

    public Team Team { get; set; } = null!;

    public WorkerNode? WorkerNode { get; set; }

    public List<TeamLabRuntimeNetwork> Networks { get; set; } = [];

    public List<TeamLabRuntimeAsset> Assets { get; set; } = [];

    public List<TeamLabVpnPeerRuntime> VpnPeers { get; set; } = [];

    public TeamLabPublicUdpMapping? PublicUdpMapping { get; set; }

    public List<TeamLabEvent> Events { get; set; } = [];
}

[Index(nameof(RuntimeId), nameof(TopologyKey), IsUnique = true)]
public class TeamLabRuntimeNetwork
{
    [Key] public int Id { get; set; }

    public int RuntimeId { get; set; }

    [MaxLength(64)] public string TopologyKey { get; set; } = string.Empty;

    [MaxLength(128)] public string Name { get; set; } = string.Empty;

    [MaxLength(64)] public string Cidr { get; set; } = string.Empty;

    [MaxLength(64)] public string GatewayIp { get; set; } = string.Empty;

    [MaxLength(128)] public string BridgeName { get; set; } = string.Empty;

    public TeamLabRuntime Runtime { get; set; } = null!;
}

[Index(nameof(RuntimeId), nameof(Kind), nameof(TopologyKey))]
public class TeamLabRuntimeAsset
{
    [Key] public int Id { get; set; }

    public int RuntimeId { get; set; }

    public TeamLabResourceKind Kind { get; set; }

    [MaxLength(64)] public string TopologyKey { get; set; } = string.Empty;

    [MaxLength(128)] public string Name { get; set; } = string.Empty;

    [MaxLength(256)] public string? RuntimeResourceId { get; set; }

    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;

    [MaxLength(1024)] public string? LastError { get; set; }

    public TeamLabRuntime Runtime { get; set; } = null!;
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

    public int ConfigVersion { get; set; } = 1;

    public bool Revoked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TeamLabRuntime Runtime { get; set; } = null!;
}

[Index(nameof(RuntimeId), IsUnique = true)]
[Index(nameof(PublicUdpPort), IsUnique = true)]
public class TeamLabPublicUdpMapping
{
    [Key] public int Id { get; set; }

    public int RuntimeId { get; set; }

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
