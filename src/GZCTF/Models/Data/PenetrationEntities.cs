using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationDeploymentStatus>))]
public enum PenetrationDeploymentStatus : byte
{
    Draft = 0,
    Published = 1,
    Deploying = 2,
    Running = 3,
    Partial = 4,
    Stopped = 5,
    Failed = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationNodeType>))]
public enum PenetrationNodeType : byte
{
    Entry = 0,
    Web = 1,
    Database = 2,
    JumpHost = 3,
    Internal = 4,
    DomainControllerReserved = 5,
    Custom = 6,
    Bastion = 7,
    FirewallRouter = 8,
    Service = 9
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationZoneType>))]
public enum PenetrationZoneType : byte
{
    Public = 0,
    Dmz = 1,
    Business = 2,
    Data = 3,
    Operations = 4,
    Management = 5,
    Custom = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationDefaultPolicy>))]
public enum PenetrationDefaultPolicy : byte
{
    DenyAll = 0,
    AllowInternal = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationPolicyScope>))]
public enum PenetrationPolicyScope : byte
{
    Node = 0,
    Network = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationPolicyAction>))]
public enum PenetrationPolicyAction : byte
{
    Allow = 0,
    Deny = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationEnforcementMode>))]
public enum PenetrationEnforcementMode : byte
{
    HintOnly = 0,
    RuntimeRoute = 1,
    Both = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationRouteStatus>))]
public enum PenetrationRouteStatus : byte
{
    HintOnly = 0,
    RoutePlanned = 1,
    RouteApplied = 2,
    RouteFailed = 3,
    Unsupported = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationProtocol>))]
public enum PenetrationProtocol : byte
{
    Tcp = 0,
    Udp = 1,
    Icmp = 2,
    Any = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationRuntimeStatus>))]
public enum PenetrationRuntimeStatus : byte
{
    Pending = 0,
    Running = 1,
    Stopped = 2,
    Failed = 3,
    CreatingNetworks = 4,
    CreatingContainers = 5,
    CleanupPending = 6,
    Orphaned = 7,
    ManualCleanupRequired = 8
}

[JsonConverter(typeof(JsonStringEnumConverter<PenetrationDeploymentEventLevel>))]
public enum PenetrationDeploymentEventLevel : byte
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

[Index(nameof(GameId), nameof(PublishedVersion), IsUnique = true)]
public class PenetrationPublishedSnapshot
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }

    public int PublishedVersion { get; set; }

    [MaxLength(128)]
    public string SnapshotHash { get; set; } = string.Empty;

    public string SnapshotJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? CreatedBy { get; set; }

    public Game Game { get; set; } = null!;
}

[Index(nameof(GameId), IsUnique = true)]
public class PenetrationConfig
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }

    [MaxLength(64)]
    public string BaseCidr { get; set; } = "10.60.0.0/12";

    public int TeamSubnetPrefix { get; set; } = 24;

    public int NetworkSubnetPrefix { get; set; } = 28;

    public int MaxResetCount { get; set; } = 3;

    public int PublishedVersion { get; set; }

    public PenetrationDeploymentStatus Status { get; set; } = PenetrationDeploymentStatus.Draft;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? DeployedAt { get; set; }

    public Game Game { get; set; } = null!;

    public List<PenetrationNetwork> Networks { get; set; } = [];

    public List<PenetrationNode> Nodes { get; set; } = [];

    public List<PenetrationEdge> Edges { get; set; } = [];
}

[Index(nameof(ConfigId))]
public class PenetrationNetwork
{
    [Key]
    public int Id { get; set; }

    public int ConfigId { get; set; }

    [Required, MaxLength(64)]
    public string TopologyKey { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? Cidr { get; set; }

    public PenetrationZoneType ZoneType { get; set; } = PenetrationZoneType.Custom;

    public int TrustLevel { get; set; } = 50;

    [MaxLength(512)]
    public string? Description { get; set; }

    public PenetrationDefaultPolicy DefaultPolicy { get; set; } = PenetrationDefaultPolicy.DenyAll;

    public int OrderIndex { get; set; }

    public bool IsEntry { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public double Width { get; set; } = 520;

    public double Height { get; set; } = 360;

    public bool Collapsed { get; set; }

    public PenetrationConfig Config { get; set; } = null!;

    public List<PenetrationNode> Nodes { get; set; } = [];

    public List<PenetrationInterface> Interfaces { get; set; } = [];
}

[Index(nameof(ConfigId))]
[Index(nameof(NetworkId))]
public class PenetrationNode
{
    [Key]
    public int Id { get; set; }

    public int ConfigId { get; set; }

    [Required, MaxLength(64)]
    public string TopologyKey { get; set; } = string.Empty;

    public int NetworkId { get; set; }

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    [MaxLength(128)]
    public string? PlayerAlias { get; set; }

    [MaxLength(512)]
    public string? PlayerDescription { get; set; }

    public PenetrationNodeType NodeType { get; set; } = PenetrationNodeType.Internal;

    public int? ImageTemplateId { get; set; }

    [MaxLength(512)]
    public string? ImageName { get; set; }

    public int CpuCount { get; set; } = 10;

    public int MemoryLimit { get; set; } = 512;

    public int StorageLimit { get; set; } = 512;

    public int ExposePort { get; set; } = 80;

    public bool IsEntry { get; set; }

    public bool PublishPort { get; set; }

    public bool AllowRouting { get; set; }

    [MaxLength(64)]
    public string? StaticIp { get; set; }

    [MaxLength(2048)]
    public string EnvironmentVariables { get; set; } = "{}";

    [MaxLength(512)]
    public string? StartCommand { get; set; }

    [MaxLength(512)]
    public string? HealthCheck { get; set; }

    [MaxLength(64)]
    public string? ReservedAdRole { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public int OrderIndex { get; set; }

    public PenetrationConfig Config { get; set; } = null!;

    public PenetrationNetwork Network { get; set; } = null!;

    public ImageTemplate? ImageTemplate { get; set; }

    public List<PenetrationScoreItem> ScoreItems { get; set; } = [];

    public List<PenetrationInterface> Interfaces { get; set; } = [];
}

[Index(nameof(NodeId))]
[Index(nameof(NetworkId))]
public class PenetrationInterface
{
    [Key]
    public int Id { get; set; }

    public int NodeId { get; set; }

    public int NetworkId { get; set; }

    [Required, MaxLength(64)]
    public string TopologyKey { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Name { get; set; } = "eth0";

    [MaxLength(64)]
    public string? StaticIp { get; set; }

    public bool IsPrimary { get; set; } = true;

    public bool IsManagement { get; set; }

    public int OrderIndex { get; set; }

    public PenetrationNode Node { get; set; } = null!;

    public PenetrationNetwork Network { get; set; } = null!;
}

[Index(nameof(ConfigId))]
public class PenetrationEdge
{
    [Key]
    public int Id { get; set; }

    public int ConfigId { get; set; }

    [Required, MaxLength(64)]
    public string TopologyKey { get; set; } = string.Empty;

    public int SourceNodeId { get; set; }

    public int TargetNodeId { get; set; }

    public PenetrationPolicyScope SourceKind { get; set; } = PenetrationPolicyScope.Node;

    public int SourceId { get; set; }

    public PenetrationPolicyScope TargetKind { get; set; } = PenetrationPolicyScope.Node;

    public int TargetId { get; set; }

    public PenetrationProtocol Protocol { get; set; } = PenetrationProtocol.Tcp;

    [MaxLength(64)]
    public string PortRange { get; set; } = "any";

    public PenetrationPolicyAction PolicyAction { get; set; } = PenetrationPolicyAction.Allow;

    public bool IsRouteHint { get; set; } = true;

    public PenetrationEnforcementMode EnforcementMode { get; set; } = PenetrationEnforcementMode.HintOnly;

    public int Priority { get; set; } = 100;

    [MaxLength(128)]
    public string? Label { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    public PenetrationConfig Config { get; set; } = null!;
}

[Index(nameof(NodeId))]
public class PenetrationScoreItem
{
    [Key]
    public int Id { get; set; }

    public int NodeId { get; set; }

    [Required, MaxLength(64)]
    public string TopologyKey { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string Category { get; set; } = "General";

    public int Score { get; set; } = 100;

    public bool IsDynamic { get; set; } = true;

    [MaxLength(Limits.MaxFlagLength)]
    public string? StaticFlag { get; set; }

    [MaxLength(Limits.MaxFlagTemplateLength)]
    public string? FlagTemplate { get; set; }

    public int MaxAttempts { get; set; }

    public bool IsVisible { get; set; } = true;

    public bool IsCheckpoint { get; set; }

    [MaxLength(512)]
    public string PrerequisiteItemIds { get; set; } = "[]";

    public int OrderIndex { get; set; }

    public PenetrationNode Node { get; set; } = null!;
}

[Index(nameof(GameId), nameof(TeamId), IsUnique = true)]
[Index(nameof(NodeId))]
public class PenetrationTeamEnvironment
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }

    public int TeamId { get; set; }

    public Guid? NodeId { get; set; }

    [MaxLength(128)]
    public string NetworkPrefix { get; set; } = string.Empty;

    public int TeamIndex { get; set; }

    public int PublishedVersion { get; set; }

    public PenetrationRuntimeStatus Status { get; set; } = PenetrationRuntimeStatus.Pending;

    public int ResetCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    [MaxLength(1024)]
    public string? LastError { get; set; }

    public int CleanupRetryCount { get; set; }

    public DateTimeOffset? NextCleanupAt { get; set; }

    public DateTimeOffset? LastCleanupAttemptAt { get; set; }

    public Game Game { get; set; } = null!;

    public Team Team { get; set; } = null!;

    [ForeignKey(nameof(NodeId))]
    public WorkerNode? Node { get; set; }

    public List<PenetrationRuntimeNode> RuntimeNodes { get; set; } = [];

    public List<PenetrationDeploymentEvent> DeploymentEvents { get; set; } = [];

    public List<PenetrationRuntimeRoute> RuntimeRoutes { get; set; } = [];
}

[Index(nameof(EnvironmentId), nameof(CreatedAt))]
public class PenetrationDeploymentEvent
{
    [Key]
    public int Id { get; set; }

    public int EnvironmentId { get; set; }

    [MaxLength(64)]
    public string Stage { get; set; } = string.Empty;

    public PenetrationDeploymentEventLevel Level { get; set; } = PenetrationDeploymentEventLevel.Info;

    [MaxLength(256)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? NodeName { get; set; }

    [MaxLength(1024)]
    public string? Detail { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PenetrationTeamEnvironment Environment { get; set; } = null!;
}

[Index(nameof(EnvironmentId))]
[Index(nameof(TopologyNodeId))]
[Index(nameof(ContainerId))]
public class PenetrationRuntimeNode
{
    [Key]
    public int Id { get; set; }

    public int EnvironmentId { get; set; }

    public int TopologyNodeId { get; set; }

    [MaxLength(64)]
    public string TopologyNodeKey { get; set; } = string.Empty;

    public Guid? ContainerId { get; set; }

    [MaxLength(128)]
    public string NetworkName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string InterfaceSummary { get; set; } = "[]";

    [MaxLength(512)]
    public string? AdminAccessUrl { get; set; }

    public int? PublicPort { get; set; }

    public PenetrationRuntimeStatus Status { get; set; } = PenetrationRuntimeStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PenetrationTeamEnvironment Environment { get; set; } = null!;

    public PenetrationNode TopologyNode { get; set; } = null!;

    public Container? Container { get; set; }
}

[Index(nameof(EnvironmentId))]
[Index(nameof(EdgeTopologyKey))]
public class PenetrationRuntimeRoute
{
    [Key]
    public int Id { get; set; }

    public int EnvironmentId { get; set; }

    [MaxLength(64)]
    public string EdgeTopologyKey { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Label { get; set; } = string.Empty;

    public PenetrationEnforcementMode EnforcementMode { get; set; } = PenetrationEnforcementMode.HintOnly;

    public PenetrationRouteStatus Status { get; set; } = PenetrationRouteStatus.RoutePlanned;

    [MaxLength(128)]
    public string? RouteNodeKey { get; set; }

    [MaxLength(128)]
    public string? RouteNodeName { get; set; }

    [MaxLength(128)]
    public string? SourceNetworkName { get; set; }

    [MaxLength(128)]
    public string? TargetNetworkName { get; set; }

    [MaxLength(64)]
    public string? SourceCidr { get; set; }

    [MaxLength(64)]
    public string? TargetCidr { get; set; }

    [MaxLength(64)]
    public string? GatewayIp { get; set; }

    [MaxLength(1024)]
    public string? CommandSummary { get; set; }

    [MaxLength(1024)]
    public string? Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? AppliedAt { get; set; }

    public PenetrationTeamEnvironment Environment { get; set; } = null!;
}

[Index(nameof(GameId), nameof(TeamId), nameof(ScoreItemId))]
public class PenetrationSubmission
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }

    public int TeamId { get; set; }

    public int ParticipationId { get; set; }

    public Guid UserId { get; set; }

    public int ScoreItemId { get; set; }

    public int PublishedVersion { get; set; }

    [MaxLength(64)]
    public string ScoreItemTopologyKey { get; set; } = string.Empty;

    [MaxLength(Limits.MaxFlagLength)]
    public string Answer { get; set; } = string.Empty;

    public AnswerResult Status { get; set; } = AnswerResult.FlagSubmitted;

    public int Score { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    public Game Game { get; set; } = null!;

    public Team Team { get; set; } = null!;

    public UserInfo User { get; set; } = null!;

    public Participation Participation { get; set; } = null!;

    public PenetrationScoreItem ScoreItem { get; set; } = null!;
}

[Index(nameof(EnvironmentId))]
public class PenetrationResetRecord
{
    [Key]
    public int Id { get; set; }

    public int EnvironmentId { get; set; }

    public Guid? UserId { get; set; }

    public bool ByAdmin { get; set; }

    public DateTimeOffset ResetAt { get; set; } = DateTimeOffset.UtcNow;

    public PenetrationTeamEnvironment Environment { get; set; } = null!;

    public UserInfo? User { get; set; }
}
