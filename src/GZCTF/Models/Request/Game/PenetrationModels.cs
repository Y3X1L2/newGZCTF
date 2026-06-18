using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Models.Request.Game;

public class PenetrationConfigModel
{
    public int GameId { get; set; }
    public string BaseCidr { get; set; } = "10.60.0.0/12";
    public int TeamSubnetPrefix { get; set; } = 24;
    public int NetworkSubnetPrefix { get; set; } = 28;
    public int MaxResetCount { get; set; } = 3;
    public int PublishedVersion { get; set; }
    public PenetrationDeploymentStatus Status { get; set; } = PenetrationDeploymentStatus.Draft;
    public List<PenetrationNetworkModel> Networks { get; set; } = [];
    public List<PenetrationNodeModel> Nodes { get; set; } = [];
    public List<PenetrationInterfaceModel> Interfaces { get; set; } = [];
    public List<PenetrationEdgeModel> Edges { get; set; } = [];
}

public class PenetrationNetworkModel
{
    public int Id { get; set; }
    public string TopologyKey { get; set; } = string.Empty;
    [Required] public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Cidr { get; set; }
    public PenetrationZoneType ZoneType { get; set; } = PenetrationZoneType.Custom;
    public int TrustLevel { get; set; } = 50;
    public string? Description { get; set; }
    public PenetrationDefaultPolicy DefaultPolicy { get; set; } = PenetrationDefaultPolicy.DenyAll;
    public int OrderIndex { get; set; }
    public bool IsEntry { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Width { get; set; } = 520;
    public double Height { get; set; } = 360;
    public bool Collapsed { get; set; }
    public string? PreviewCidr { get; set; }
}

public class PenetrationNodeModel
{
    public int Id { get; set; }
    public string TopologyKey { get; set; } = string.Empty;
    public int NetworkId { get; set; }
    [Required] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PenetrationNodeType NodeType { get; set; } = PenetrationNodeType.Internal;
    public int? ImageTemplateId { get; set; }
    public string? ImageName { get; set; }
    public int CpuCount { get; set; } = 10;
    public int MemoryLimit { get; set; } = 512;
    public int StorageLimit { get; set; } = 512;
    public int ExposePort { get; set; } = 80;
    public bool IsEntry { get; set; }
    public bool PublishPort { get; set; }
    public string? StaticIp { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public string? StartCommand { get; set; }
    public string? HealthCheck { get; set; }
    public string? ReservedAdRole { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public int OrderIndex { get; set; }
    public string? PreviewIp { get; set; }
    public List<PenetrationInterfaceModel> Interfaces { get; set; } = [];
    public List<PenetrationScoreItemModel> ScoreItems { get; set; } = [];
}

public class PenetrationInterfaceModel
{
    public int Id { get; set; }
    public string TopologyKey { get; set; } = string.Empty;
    public int NodeId { get; set; }
    public int NetworkId { get; set; }
    public string Name { get; set; } = "eth0";
    public string? StaticIp { get; set; }
    public string? PreviewIp { get; set; }
    public bool IsPrimary { get; set; } = true;
    public bool IsManagement { get; set; }
    public int OrderIndex { get; set; }
}

public class PenetrationEdgeModel
{
    public int Id { get; set; }
    public string TopologyKey { get; set; } = string.Empty;
    public int SourceNodeId { get; set; }
    public int TargetNodeId { get; set; }
    public PenetrationPolicyScope SourceKind { get; set; } = PenetrationPolicyScope.Node;
    public int SourceId { get; set; }
    public PenetrationPolicyScope TargetKind { get; set; } = PenetrationPolicyScope.Node;
    public int TargetId { get; set; }
    public PenetrationProtocol Protocol { get; set; } = PenetrationProtocol.Tcp;
    public string PortRange { get; set; } = "any";
    public PenetrationPolicyAction PolicyAction { get; set; } = PenetrationPolicyAction.Allow;
    public bool IsRouteHint { get; set; } = true;
    public string? Label { get; set; }
    public string? Description { get; set; }
}

public class PenetrationScoreItemModel
{
    public int Id { get; set; }
    public string TopologyKey { get; set; } = string.Empty;
    [Required] public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "General";
    public int Score { get; set; } = 100;
    public bool IsDynamic { get; set; } = true;
    public string? StaticFlag { get; set; }
    public string? FlagTemplate { get; set; }
    public int MaxAttempts { get; set; }
    public bool IsVisible { get; set; } = true;
    public List<int> PrerequisiteItemIds { get; set; } = [];
    public int OrderIndex { get; set; }
}

public class PenetrationValidationModel
{
    public bool Valid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class PenetrationPlanModel
{
    public int GameId { get; set; }
    public int TeamCount { get; set; }
    public string SampleTeamPrefix { get; set; } = string.Empty;
    public PenetrationValidationModel Validation { get; set; } = new();
    public List<PenetrationPlanNetworkModel> Networks { get; set; } = [];
    public List<PenetrationPlanNodeModel> Nodes { get; set; } = [];
    public List<PenetrationPlanPolicyModel> Policies { get; set; } = [];
    public List<PenetrationPlanFlagModel> Flags { get; set; } = [];
    public List<string> DeploymentSteps { get; set; } = [];
}

public class PenetrationPlanNetworkModel
{
    public int NetworkId { get; set; }
    public string NetworkName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public PenetrationZoneType ZoneType { get; set; }
    public string Cidr { get; set; } = string.Empty;
    public PenetrationDefaultPolicy DefaultPolicy { get; set; }
    public bool IsInternal { get; set; }
}

public class PenetrationPlanNodeModel
{
    public int NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public PenetrationNodeType NodeType { get; set; }
    public string Image { get; set; } = string.Empty;
    public bool PublishPort { get; set; }
    public int ExposePort { get; set; }
    public List<PenetrationPlanInterfaceModel> Interfaces { get; set; } = [];
    public string? AdminAccessHint { get; set; }
}

public class PenetrationPlanInterfaceModel
{
    public int InterfaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int NetworkId { get; set; }
    public string NetworkName { get; set; } = string.Empty;
    public string NetworkSlug { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsManagement { get; set; }
    public bool IsInternal { get; set; }
}

public class PenetrationPlanPolicyModel
{
    public int PolicyId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public PenetrationProtocol Protocol { get; set; }
    public string PortRange { get; set; } = "any";
    public PenetrationPolicyAction Action { get; set; }
    public bool IsRouteHint { get; set; }
}

public class PenetrationPlanFlagModel
{
    public int ScoreItemId { get; set; }
    public int NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool IsDynamic { get; set; }
    public string Preview { get; set; } = string.Empty;
}

public class PenetrationWorkspaceModel
{
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public PenetrationRuntimeStatus Status { get; set; }
    public int ResetCount { get; set; }
    public int MaxResetCount { get; set; }
    public List<PenetrationEntryPointModel> EntryPoints { get; set; } = [];
    public List<PenetrationWorkspaceNetworkModel> Networks { get; set; } = [];
    public List<PenetrationWorkspaceNodeModel> Nodes { get; set; } = [];
    public List<PenetrationWorkspacePolicyModel> Policies { get; set; } = [];
}

public class PenetrationWorkspaceNetworkModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public PenetrationZoneType ZoneType { get; set; }
    public int TrustLevel { get; set; }
    public int OrderIndex { get; set; }
    public bool IsEntry { get; set; }
    public string? Cidr { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class PenetrationWorkspacePolicyModel
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int SourceNodeId { get; set; }
    public int TargetNodeId { get; set; }
    public PenetrationProtocol Protocol { get; set; }
    public string PortRange { get; set; } = string.Empty;
}

public class PenetrationTeamEnvironmentModel
{
    public int EnvironmentId { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public Guid? WorkerNodeId { get; set; }
    public string? WorkerNodeName { get; set; }
    public string NetworkPrefix { get; set; } = string.Empty;
    public int PublishedVersion { get; set; }
    public PenetrationRuntimeStatus Status { get; set; }
    public int ResetCount { get; set; }
    public int RuntimeNodeCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? LastError { get; set; }
}

public class PenetrationEntryPointModel
{
    public int NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public int ExposePort { get; set; }
}

public class PenetrationWorkspaceNodeModel
{
    public int Id { get; set; }
    public int NetworkId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PenetrationNodeType NodeType { get; set; }
    public string? IpAddress { get; set; }
    public bool IsEntry { get; set; }
    public PenetrationRuntimeStatus RuntimeStatus { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public List<PenetrationInterfaceModel> Interfaces { get; set; } = [];
    public List<PenetrationWorkspaceScoreItemModel> ScoreItems { get; set; } = [];
}

public class PenetrationWorkspaceScoreItemModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool Solved { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public List<int> PrerequisiteItemIds { get; set; } = [];
}

public class PenetrationAdminAccessModel
{
    public int RuntimeNodeId { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public PenetrationRuntimeStatus Status { get; set; }
    public string WorkerNodeName { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string InternalIp { get; set; } = string.Empty;
    public string InterfaceSummary { get; set; } = string.Empty;
    public string? Host { get; set; }
    public int? PublicPort { get; set; }
    public string? Url { get; set; }
    public int ExposePort { get; set; }
}

public class PenetrationSubmitModel
{
    public int ScoreItemId { get; set; }
    [Required] public string Flag { get; set; } = string.Empty;
}

public class PenetrationSubmitResultModel
{
    public bool Accepted { get; set; }
    public int Score { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class PenetrationScoreboardItemModel
{
    public int Rank { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Score { get; set; }
    public int SolvedCount { get; set; }
    public DateTimeOffset LastSubmissionTime { get; set; }
}

public class PenetrationSubmissionLogModel
{
    public int Id { get; set; }
    public DateTimeOffset Time { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string ItemTitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Score { get; set; }
    public AnswerResult Status { get; set; }
}
