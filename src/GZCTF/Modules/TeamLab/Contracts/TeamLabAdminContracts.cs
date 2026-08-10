using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabAdminReleaseSummaryModel(
    Guid Id,
    int Version,
    int SourceRevision,
    string ContentHash,
    DateTimeOffset PublishedAt);

public sealed record TeamLabAdminValidationSummaryModel(
    int Revision,
    bool Valid,
    int IssueCount,
    DateTimeOffset ValidatedAt);

public sealed record TeamLabAdminRuntimeSummaryModel(
    Guid Id,
    Guid ReleaseId,
    TeamLabRuntimeStatus Status,
    string Stage,
    bool OpenForAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Error);

public sealed record TeamLabAdminSceneSummaryModel(
    Guid Id,
    string Name,
    Guid? OwnerId,
    string OwnerDisplayName,
    int Revision,
    int SchemaVersion,
    int NetworkCount,
    int AssetCount,
    int InfrastructureCount,
    TeamLabAdminReleaseSummaryModel? LatestRelease,
    TeamLabAdminValidationSummaryModel? Validation,
    TeamLabAdminRuntimeSummaryModel? LatestTrialRuntime,
    int GameReferenceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamLabAdminScenePageModel(
    IReadOnlyList<TeamLabAdminSceneSummaryModel> Items,
    string? NextCursor);

public sealed record TeamLabAdminImageReadinessModel(
    int ImageTemplateId,
    string Name,
    ImageType ImageType,
    string Digest,
    int EligibleNodeCount,
    int ReadyNodeCount,
    int PendingNodeCount,
    int FailedNodeCount);

/// <summary>Names a required runtime-only bootstrap secret without exposing its value.</summary>
public sealed record TeamLabRequiredRuntimeSecretModel(
    string AssetKey,
    string AssetName,
    string ParameterKey);

public sealed record TeamLabAdminReleaseReadinessModel(
    Guid TopologyId,
    Guid ReleaseId,
    bool Ready,
    TeamLabPlanModel? Plan,
    IReadOnlyList<TeamLabAdminImageReadinessModel> Images,
    TeamLabAdminRuntimeSummaryModel? LatestTrialRuntime,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<TeamLabRequiredRuntimeSecretModel> RequiredRuntimeSecrets);

public sealed record TeamLabAdminRuntimePageModel(
    IReadOnlyList<TeamLabAdminRuntimeSummaryModel> Items,
    string? NextCursor);

public sealed record CreateTeamLabTrialRuntimeModel(
    Guid ReleaseId,
    TeamLabRuntimeConstraintsModel? Constraints,
    IReadOnlyList<TeamLabRuntimeOverlayModel>? Overlays,
    string? ExternalReference = null);
