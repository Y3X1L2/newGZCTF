using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record OpenCreateTeamLabTopologyModel(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections,
    TeamLabTopologyEditorModel? Editor = null,
    IReadOnlyList<TeamLabTopologyInfrastructureModel>? Infrastructure = null,
    IReadOnlyList<TeamLabTopologyDependencyModel>? Dependencies = null,
    TeamLabObservationPolicyModel? Observation = null,
    int SchemaVersion = 2,
    Guid? ControlScopeId = null);

public sealed record OpenUpdateTeamLabTopologyModel(
    int Revision,
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections,
    TeamLabTopologyEditorModel? Editor = null,
    IReadOnlyList<TeamLabTopologyInfrastructureModel>? Infrastructure = null,
    IReadOnlyList<TeamLabTopologyDependencyModel>? Dependencies = null,
    TeamLabObservationPolicyModel? Observation = null,
    int SchemaVersion = 2);

public sealed record OpenTeamLabTopologyDetailModel(
    Guid Id,
    Guid? ControlScopeId,
    int Revision,
    int SchemaVersion,
    TeamLabTopologyDefinitionModel Definition,
    TeamLabTopologyEditorModel Editor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OpenTeamLabTopologyPageModel(
    IReadOnlyList<TeamLabTopologySummaryModel> Items,
    string? NextCursor);

public sealed record OpenTeamLabReleaseModel(
    Guid Id,
    Guid TopologyId,
    int Version,
    int SourceRevision,
    int SchemaVersion,
    string ContentHash,
    DateTimeOffset PublishedAt,
    TeamLabTopologyEditorModel? Editor = null,
    bool Archived = false);

public sealed record OpenTeamLabReleasePageModel(
    IReadOnlyList<OpenTeamLabReleaseModel> Items,
    string? NextCursor);

public sealed record OpenTeamLabFailureModel(
    string Code,
    string Stage,
    bool Retryable,
    IReadOnlyList<string>? Actions = null,
    string? ResourceType = null,
    string? ResourceId = null,
    string? Detail = null);

public sealed record OpenTeamLabRuntimeSubStageModel(
    string Id,
    string Status,
    string? Message);

public sealed record OpenTeamLabRuntimeShardModel(
    Guid Id,
    TeamLabRuntimeStatus Status,
    IReadOnlyList<string> NetworkKeys,
    IReadOnlyList<string> AssetKeys,
    OpenTeamLabFailureModel? Failure);

public sealed record OpenTeamLabRuntimeAssetModel(
    string Key,
    string Name,
    TeamLabAssetKind Kind,
    string? PrimaryIp,
    TeamLabRuntimeStatus Status,
    OpenTeamLabFailureModel? Failure);

public sealed record OpenTeamLabRuntimeModel(
    Guid Id,
    Guid ReleaseId,
    int Generation,
    TeamLabExecutionModel ExecutionModel,
    TeamLabRuntimeStatus Status,
    string Stage,
    bool OpenForAccess,
    IReadOnlyList<OpenTeamLabRuntimeShardModel> Shards,
    IReadOnlyList<TeamLabRuntimeNetworkProjectionModel> Networks,
    IReadOnlyList<OpenTeamLabRuntimeAssetModel> Assets,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    OpenTeamLabFailureModel? Failure,
    Guid? CurrentOperationId = null,
    Guid? DeploymentQueueTicketId = null,
    DeploymentQueueTicketStatus? QueueStatus = null,
    IReadOnlyList<OpenTeamLabRuntimeSubStageModel>? SubStages = null,
    Guid? ControlScopeId = null,
    int? ReleaseVersion = null,
    IReadOnlyList<string>? RecoveryActions = null);

public sealed record OpenTeamLabRuntimeEventPageModel(
    IReadOnlyList<TeamLabRuntimeEventModel> Items,
    string? NextCursor);

public sealed record OpenTeamLabCaptureModel(
    Guid Id,
    TeamLabTrafficCaptureStatus Status,
    string Scope,
    string? NetworkKey,
    long MaxBytes,
    int MaxSeconds,
    long CapturedBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<TeamLabCaptureSegmentModel> Segments,
    OpenTeamLabFailureModel? Failure);

public sealed record OpenTeamLabCapturePageModel(
    IReadOnlyList<OpenTeamLabCaptureModel> Items,
    string? Next);

public static class OpenTeamLabContractMapper
{
    public static CreateTeamLabTopologyModel ToInternal(this OpenCreateTeamLabTopologyModel model) =>
        new(model.Name, model.Networks, model.Assets, model.Connections, model.Editor,
            Infrastructure: model.Infrastructure,
            Dependencies: model.Dependencies,
            Observation: model.Observation,
            SchemaVersion: model.SchemaVersion,
            ControlScopeId: model.ControlScopeId);

    public static UpdateTeamLabTopologyModel ToInternal(this OpenUpdateTeamLabTopologyModel model) =>
        new(model.Revision, model.Name, model.Networks, model.Assets, model.Connections, model.Editor,
            Infrastructure: model.Infrastructure,
            Dependencies: model.Dependencies,
            Observation: model.Observation,
            SchemaVersion: model.SchemaVersion);

    public static OpenTeamLabTopologyDetailModel ToOpen(this TeamLabTopologyDetailModel model) =>
        new(model.Id, model.ControlScopeId, model.Revision, model.SchemaVersion, model.Definition, model.Editor, model.CreatedAt, model.UpdatedAt);

    public static OpenTeamLabReleaseModel ToOpen(this TeamLabReleaseModel model) =>
        new(model.Id, model.TopologyId, model.Version, model.SourceRevision, model.SchemaVersion,
            model.ContentHash, model.PublishedAt, model.Editor, model.Archived);

    public static OpenTeamLabRuntimeModel ToOpen(this TeamLabRuntimeProjectionModel model) =>
        new(
            model.Id,
            model.ReleaseId,
            model.Generation,
            model.ExecutionModel,
            model.Status,
            model.Stage,
            model.OpenForAccess,
            model.Shards.Select(item => new OpenTeamLabRuntimeShardModel(
                item.Id,
                item.Status,
                item.NetworkKeys,
                item.AssetKeys,
                Failure(item.Failure))).ToArray(),
            model.Networks,
            model.Assets.Select(item => new OpenTeamLabRuntimeAssetModel(
                item.Key,
                item.Name,
                item.Kind,
                item.PrimaryIp,
                item.Status,
                Failure(item.Failure))).ToArray(),
            model.CreatedAt,
            model.UpdatedAt,
            Failure(model.Failure),
            model.CurrentOperationId,
            model.DeploymentQueueTicketId,
            model.QueueStatus,
            model.SubStages?.Select(item => new OpenTeamLabRuntimeSubStageModel(
                item.Id, item.Status, item.Message)).ToArray(),
            model.ControlScopeId,
            model.ReleaseVersion,
            model.RecoveryActions);

    public static OpenTeamLabCaptureModel ToOpen(this TeamLabCaptureModel model) =>
        new(model.Id, model.Status, model.Scope, model.NetworkKey, model.MaxBytes, model.MaxSeconds,
            model.CapturedBytes, model.CreatedAt, model.StartedAt, model.CompletedAt, model.ExpiresAt, model.Segments,
            Failure(model.Status == TeamLabTrafficCaptureStatus.Failed, model.Error,
                "capture", "teamlab_capture_failed"));

    private static OpenTeamLabFailureModel? Failure(
        bool failed,
        string? error,
        string stage,
        string code) =>
        !failed && string.IsNullOrWhiteSpace(error) ? null : new OpenTeamLabFailureModel(code, stage, false);

    private static OpenTeamLabFailureModel? Failure(TeamLabFailureProjectionModel? failure) =>
        failure is null
            ? null
            : new OpenTeamLabFailureModel(
                failure.Code,
                failure.Stage,
                failure.Retryable,
                failure.Actions,
                failure.ResourceType,
                failure.ResourceId,
                failure.Detail);
}
