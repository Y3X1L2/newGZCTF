using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record OpenCreateTeamLabTopologyModel(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections);

public sealed record OpenUpdateTeamLabTopologyModel(
    int Revision,
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections);

public sealed record OpenTeamLabTopologyDetailModel(
    Guid Id,
    int Revision,
    int SchemaVersion,
    TeamLabTopologyDefinitionModel Definition,
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
    DateTimeOffset PublishedAt);

public sealed record OpenTeamLabReleasePageModel(
    IReadOnlyList<OpenTeamLabReleaseModel> Items,
    string? NextCursor);

public sealed record OpenTeamLabFailureModel(
    string Code,
    string Stage,
    bool Retryable);

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
    TeamLabRuntimeStatus Status,
    string Stage,
    bool OpenForAccess,
    IReadOnlyList<OpenTeamLabRuntimeShardModel> Shards,
    IReadOnlyList<TeamLabRuntimeNetworkProjectionModel> Networks,
    IReadOnlyList<OpenTeamLabRuntimeAssetModel> Assets,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    OpenTeamLabFailureModel? Failure);

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
    OpenTeamLabFailureModel? Failure);

public static class OpenTeamLabContractMapper
{
    public static CreateTeamLabTopologyModel ToInternal(this OpenCreateTeamLabTopologyModel model) =>
        new(model.Name, model.Networks, model.Assets, model.Connections);

    public static UpdateTeamLabTopologyModel ToInternal(this OpenUpdateTeamLabTopologyModel model) =>
        new(model.Revision, model.Name, model.Networks, model.Assets, model.Connections);

    public static OpenTeamLabTopologyDetailModel ToOpen(this TeamLabTopologyDetailModel model) =>
        new(model.Id, model.Revision, model.SchemaVersion, model.Definition, model.CreatedAt, model.UpdatedAt);

    public static OpenTeamLabReleaseModel ToOpen(this TeamLabReleaseModel model) =>
        new(model.Id, model.TopologyId, model.Version, model.SourceRevision, model.SchemaVersion,
            model.ContentHash, model.PublishedAt);

    public static OpenTeamLabRuntimeModel ToOpen(this TeamLabRuntimeProjectionModel model) =>
        new(
            model.Id,
            model.ReleaseId,
            model.Generation,
            model.Status,
            model.Stage,
            model.OpenForAccess,
            model.Shards.Select(item => new OpenTeamLabRuntimeShardModel(
                item.Id,
                item.Status,
                item.NetworkKeys,
                item.AssetKeys,
                Failure(item.Status == TeamLabRuntimeStatus.Failed, item.Error,
                    "shard", "teamlab_shard_failed"))).ToArray(),
            model.Networks,
            model.Assets.Select(item => new OpenTeamLabRuntimeAssetModel(
                item.Key,
                item.Name,
                item.Kind,
                item.PrimaryIp,
                item.Status,
                Failure(item.Status == TeamLabRuntimeStatus.Failed, item.Error,
                    "asset", "teamlab_asset_failed"))).ToArray(),
            model.CreatedAt,
            model.UpdatedAt,
            Failure(model.Status is TeamLabRuntimeStatus.Failed or TeamLabRuntimeStatus.CleanupPending,
                model.Error, model.Stage, "teamlab_runtime_failed"));

    public static OpenTeamLabCaptureModel ToOpen(this TeamLabCaptureModel model) =>
        new(model.Id, model.Status, model.Scope, model.NetworkKey, model.MaxBytes, model.MaxSeconds,
            model.CapturedBytes, model.CreatedAt, model.StartedAt, model.CompletedAt, model.ExpiresAt,
            Failure(model.Status == TeamLabTrafficCaptureStatus.Failed, model.Error,
                "capture", "teamlab_capture_failed"));

    private static OpenTeamLabFailureModel? Failure(
        bool failed,
        string? error,
        string stage,
        string code) =>
        !failed && string.IsNullOrWhiteSpace(error) ? null : new OpenTeamLabFailureModel(code, stage, false);
}
