using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Models.Request.Game;

public enum PenetrationFogState
{
    Hidden = 0,
    Revealed = 1,
    Accessible = 2,
    Completed = 3
}

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
    public string? PlayerAlias { get; set; }
    public string? PlayerDescription { get; set; }
    public PenetrationNodeType NodeType { get; set; } = PenetrationNodeType.Internal;
    public int? ImageTemplateId { get; set; }
    public string? ImageName { get; set; }
    public int CpuCount { get; set; } = 10;
    public int MemoryLimit { get; set; } = 512;
    public int StorageLimit { get; set; } = 512;
    public int ExposePort { get; set; } = 80;
    public bool IsEntry { get; set; }
    public bool PublishPort { get; set; }
    public bool AllowRouting { get; set; }
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
    public PenetrationEnforcementMode EnforcementMode { get; set; } = PenetrationEnforcementMode.HintOnly;
    public int Priority { get; set; } = 100;
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
    public bool IsCheckpoint { get; set; }
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
    public PenetrationEnforcementMode EnforcementMode { get; set; }
    public PenetrationRouteStatus RouteStatus { get; set; }
    public string RuntimeSummary { get; set; } = string.Empty;
    public string? RouteNodeName { get; set; }
    public string? SourceNetworkName { get; set; }
    public string? TargetNetworkName { get; set; }
    public string? GatewayIp { get; set; }
    public string? CompileMessage { get; set; }
    public bool IsExecutable { get; set; }
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
    public PenetrationAttackGraphModel AttackGraph { get; set; } = new();
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
    public int TeamIndex { get; set; }
    public int PublishedVersion { get; set; }
    public PenetrationRuntimeStatus Status { get; set; }
    public int ResetCount { get; set; }
    public int RuntimeNodeCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? LastError { get; set; }
    public int CleanupRetryCount { get; set; }
    public DateTimeOffset? NextCleanupAt { get; set; }
    public DateTimeOffset? LastCleanupAttemptAt { get; set; }
    public List<PenetrationDeploymentEventModel> Events { get; set; } = [];
    public List<PenetrationRuntimeNodeModel> RuntimeNodes { get; set; } = [];
    public List<PenetrationRuntimeRouteModel> RuntimeRoutes { get; set; } = [];
}

public class PenetrationRuntimeNodeModel
{
    public int RuntimeNodeId { get; set; }
    public int TopologyNodeId { get; set; }
    public string TopologyNodeKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string NetworkName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string? AdminAccessUrl { get; set; }
    public int? PublicPort { get; set; }
    public PenetrationRuntimeStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ContainerGuid { get; set; }
    public string? ContainerId { get; set; }
    public ContainerStatus? ContainerStatus { get; set; }
    public string? Image { get; set; }
    public string? PublicHost { get; set; }
    public string InterfaceSummary { get; set; } = "[]";
}

public class PenetrationRuntimeRouteModel
{
    public int Id { get; set; }
    public string EdgeTopologyKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public PenetrationEnforcementMode EnforcementMode { get; set; }
    public PenetrationRouteStatus Status { get; set; }
    public string? RouteNodeKey { get; set; }
    public string? RouteNodeName { get; set; }
    public string? SourceNetworkName { get; set; }
    public string? TargetNetworkName { get; set; }
    public string? SourceCidr { get; set; }
    public string? TargetCidr { get; set; }
    public string? GatewayIp { get; set; }
    public string? CommandSummary { get; set; }
    public string? Message { get; set; }
    public bool IsExecutable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}

public class PenetrationDeploymentEventModel
{
    public int Id { get; set; }
    public int EnvironmentId { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public PenetrationDeploymentEventLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NodeName { get; set; }
    public string? Detail { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
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
    public string TopologyKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PenetrationNodeType NodeType { get; set; }
    public string? IpAddress { get; set; }
    public bool IsEntry { get; set; }
    public PenetrationFogState FogState { get; set; }
    public PenetrationRuntimeStatus RuntimeStatus { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public List<PenetrationInterfaceModel> Interfaces { get; set; } = [];
    public List<PenetrationWorkspaceScoreItemModel> ScoreItems { get; set; } = [];
}

public class PenetrationWorkspaceScoreItemModel
{
    public int Id { get; set; }
    public string TopologyKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool Solved { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public bool IsCheckpoint { get; set; }
    public List<int> PrerequisiteItemIds { get; set; } = [];
    public List<string> PrerequisiteItemKeys { get; set; } = [];
}

public class PenetrationAttackGraphModel
{
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public int PublishedVersion { get; set; }
    public int TotalNodeCount { get; set; }
    public int VisibleNodeCount { get; set; }
    public int CompletedNodeCount { get; set; }
    public int TotalScoreItemCount { get; set; }
    public int SolvedScoreItemCount { get; set; }
    public List<PenetrationAttackNodeModel> Nodes { get; set; } = [];
    public List<PenetrationAttackEdgeModel> Edges { get; set; } = [];
}

public class PenetrationAttackNodeModel
{
    public int Id { get; set; }
    public string TopologyKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Depth { get; set; }
    public PenetrationFogState Status { get; set; }
    public PenetrationAttackScoreSummaryModel ScoreSummary { get; set; } = new();
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public bool IsEntry { get; set; }
    public bool IsCheckpointCompleted { get; set; }
    public PenetrationRuntimeStatus RuntimeStatus { get; set; }
}

public class PenetrationAttackScoreSummaryModel
{
    public int Total { get; set; }
    public int Solved { get; set; }
    public int CheckpointTotal { get; set; }
    public int CheckpointSolved { get; set; }
    public int TotalScore { get; set; }
    public int SolvedScore { get; set; }
}

public class PenetrationAttackEdgeModel
{
    public int Id { get; set; }
    public string SourceNodeKey { get; set; } = string.Empty;
    public string TargetNodeKey { get; set; } = string.Empty;
    public PenetrationFogState Status { get; set; }
    public string Label { get; set; } = string.Empty;
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
    public bool AttackGraphChanged { get; set; }
    public int UnlockedNodeCount { get; set; }
}

public class PenetrationAttackGraphUpdateModel
{
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public int PublishedVersion { get; set; }
    public bool Accepted { get; set; }
    public bool GraphChanged { get; set; }
    public int CompletedNodeCount { get; set; }
    public int VisibleNodeCount { get; set; }
    public int UnlockedNodeCount { get; set; }
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
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
