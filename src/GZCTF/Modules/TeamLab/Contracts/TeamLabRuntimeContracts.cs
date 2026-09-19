using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabRuntimeConstraintsModel(
    string? PreferredRegion,
    IReadOnlyList<string> RequiredCapabilities);

public sealed record TeamLabRuntimeOverlayModel(
    string AssetKey,
    IReadOnlyDictionary<string, string>? Secrets);

public sealed record CreateTeamLabRuntimeModel(
    Guid ReleaseId,
    string? ExternalReference,
    TeamLabRuntimeConstraintsModel? Constraints,
    IReadOnlyList<TeamLabRuntimeOverlayModel>? Overlays);

public sealed record ResetTeamLabRuntimeModel(
    IReadOnlyList<TeamLabRuntimeOverlayModel>? Overlays,
    Guid? ReleaseId = null);

public sealed record UpdateTeamLabRuntimeModel(
    Guid ReleaseId,
    IReadOnlyList<TeamLabRuntimeOverlayModel>? Overlays = null);

public sealed record ChangeTeamLabRuntimeAssetsModel(
    int ExpectedPlanRevision,
    IReadOnlyList<TeamLabTopologyAssetModel>? Add = null,
    IReadOnlyList<TeamLabTopologyAssetModel>? Replace = null,
    IReadOnlyList<string>? Remove = null,
    IReadOnlyList<TeamLabRuntimeOverlayModel>? Overlays = null);

public sealed record TeamLabRuntimeUpdateChangeModel(
    string AssetKey,
    string AssetName,
    TeamLabAssetKind Kind,
    string Action);

public sealed record TeamLabRuntimeUpdatePreviewModel(
    Guid RuntimeId,
    Guid CurrentReleaseId,
    Guid TargetReleaseId,
    int CurrentPlanRevision,
    bool CanApply,
    string? ResetRequiredReason,
    IReadOnlyList<TeamLabRuntimeUpdateChangeModel> Changes);

public sealed record TeamLabRuntimeGrantWriteModel(
    string SubjectType,
    Guid SubjectId,
    string? AssetKey,
    IReadOnlyList<string> Permissions);

public sealed record ReplaceTeamLabRuntimeGrantsModel(
    IReadOnlyList<TeamLabRuntimeGrantWriteModel> Grants);

public sealed record TeamLabRuntimeGrantModel(
    long Id,
    string SubjectType,
    Guid SubjectId,
    string SubjectName,
    string? AssetKey,
    IReadOnlyList<string> Permissions,
    DateTimeOffset UpdatedAt);

public sealed record PrepareTeamLabTemplatesModel(IReadOnlyList<int> TemplateIds);

public sealed record TeamLabTemplatePreparationResultModel(
    int TemplateCount,
    int DistributionCount);

public sealed record TeamLabRuntimeShardProjectionModel(
    Guid Id,
    Guid WorkerNodeId,
    string WorkerNodeName,
    TeamLabRuntimeStatus Status,
    IReadOnlyList<string> NetworkKeys,
    IReadOnlyList<string> AssetKeys,
    string? Error,
    TeamLabFailureProjectionModel? Failure = null);

public sealed record TeamLabRuntimeNetworkProjectionModel(
    string Key,
    string Name,
    string Cidr,
    string GatewayIp);

public sealed record TeamLabRuntimeAssetProjectionModel(
    int Id,
    string Key,
    string Name,
    TeamLabAssetKind Kind,
    IReadOnlyList<string> NetworkKeys,
    string? RuntimeResourceId,
    string? PrimaryIp,
    TeamLabRuntimeStatus Status,
    string? Error,
    TeamLabFailureProjectionModel? Failure = null);

public sealed record TeamLabFailureProjectionModel(
    string Code,
    string Stage,
    bool Retryable,
    IReadOnlyList<string> Actions,
    string? ResourceType,
    string? ResourceId,
    string? Detail);

public sealed record TeamLabRuntimeSubStageProjectionModel(
    string Id,
    string Status,
    string? Message);

public sealed record TeamLabRuntimeProjectionModel(
    Guid Id,
    Guid ReleaseId,
    int Generation,
    TeamLabRuntimeStatus Status,
    string Stage,
    bool OpenForAccess,
    IReadOnlyList<TeamLabRuntimeShardProjectionModel> Shards,
    IReadOnlyList<TeamLabRuntimeNetworkProjectionModel> Networks,
    IReadOnlyList<TeamLabRuntimeAssetProjectionModel> Assets,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Error,
    Guid? CurrentOperationId = null,
    Guid? DeploymentQueueTicketId = null,
    DeploymentQueueTicketStatus? QueueStatus = null,
    IReadOnlyList<TeamLabRuntimeSubStageProjectionModel>? SubStages = null,
    Guid? ControlScopeId = null,
    int? ReleaseVersion = null,
    IReadOnlyList<string>? RecoveryActions = null,
    TeamLabFailureProjectionModel? Failure = null,
    Guid? ManagedRolloutId = null,
    int PlanRevision = 0);

public sealed record TeamLabRuntimeEventModel(
    long Cursor,
    int Generation,
    string Stage,
    TeamLabEventLevel Level,
    string Message,
    string? ObjectType,
    string? ObjectId,
    DateTimeOffset CreatedAt);

public sealed record TeamLabAccessGrantCreateModel(string Type = "WireGuard");

public sealed record TeamLabAccessGrantModel(
    Guid Id,
    string Type,
    string ClientAddress,
    string Endpoint,
    string AllowedIps,
    string Dns,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? ConfigurationDownloadUrl);

public sealed record TeamLabRuntimeCreateResult(int RuntimeId, Guid RuntimePublicId, bool Reused);
