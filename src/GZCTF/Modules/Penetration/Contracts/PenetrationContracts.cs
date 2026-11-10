using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.Penetration.Contracts;

public sealed record PenetrationObjectiveWriteModel(
    string Key,
    string AssetKey,
    string Title,
    string? Description,
    string Category,
    int Score,
    bool Dynamic,
    string? StaticFlag,
    string? FlagTemplate,
    int MaxAttempts,
    bool Visible,
    bool Checkpoint,
    IReadOnlyList<string>? PrerequisiteKeys,
    int OrderIndex,
    int? Id = null);

public sealed record ReplacePenetrationObjectivesModel(
    long Revision,
    int MaxResetCount,
    IReadOnlyList<PenetrationObjectiveWriteModel> Objectives);

public sealed record PenetrationObjectiveModel(
    int Id,
    string Key,
    string AssetKey,
    string Title,
    string? Description,
    string Category,
    int Score,
    bool Dynamic,
    int MaxAttempts,
    bool Visible,
    bool Checkpoint,
    IReadOnlyList<string> PrerequisiteKeys,
    int OrderIndex);

public sealed record PenetrationGameLabBindingModel(
    int GameId,
    string TopologyId,
    string? ActiveReleaseId,
    int MaxResetCount,
    long ObjectiveRevision,
    IReadOnlyList<PenetrationObjectiveModel> Objectives);

public sealed record PenetrationReleaseOptionModel(
    string TopologyId,
    string TopologyName,
    string ReleaseId,
    int Version,
    int NetworkCount,
    int AssetCount,
    DateTimeOffset PublishedAt);

public sealed record PenetrationGameTeamLabModel(
    PenetrationGameLabBindingModel? Binding,
    PenetrationSceneSummary? Rollout);

public sealed record PenetrationWorkspaceObjectiveModel(
    int Id,
    string Key,
    string AssetKey,
    string Title,
    string? Description,
    string Category,
    int Score,
    bool Solved,
    int Attempts,
    int MaxAttempts,
    bool Checkpoint,
    IReadOnlyList<string> PrerequisiteKeys);

public sealed record PenetrationWorkspaceModel(
    int GameId,
    int TeamId,
    string TeamName,
    string RuntimeId,
    TeamLabRuntimeStatus Status,
    string Stage,
    int ResetCount,
    int MaxResetCount,
    IReadOnlyList<PenetrationWorkspaceObjectiveModel> Objectives);

public sealed record PenetrationSubmitModel(
    int ObjectiveId,
    [Required] string Flag);

public sealed record PenetrationSubmitResultModel(bool Accepted, int Score, string Message);

public sealed record PenetrationScoreboardItemModel(
    int Rank,
    int TeamId,
    string TeamName,
    int Score,
    int SolvedCount,
    DateTimeOffset LastSubmissionTime);

public sealed record PenetrationSubmissionLogModel(
    int Id,
    DateTimeOffset Time,
    int TeamId,
    string TeamName,
    string UserName,
    string AssetKey,
    string ObjectiveTitle,
    string Category,
    int Score,
    AnswerResult Status);

public sealed record PenetrationSubmissionPageModel(
    IReadOnlyList<PenetrationSubmissionLogModel> Items,
    int Total);

public sealed record PenetrationRuntimeBindingModel(
    int TeamId,
    string TeamName,
    string RuntimeId,
    int Generation,
    TeamLabRuntimeStatus Status,
    string Stage,
    int ShardCount,
    int AssetCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Error);

public sealed record PenetrationWorkspaceUpdateModel(
    int GameId,
    int TeamId,
    string RuntimeId,
    DateTimeOffset Time);


public sealed record PenetrationSceneAsset(string Id, string Name, string Kind);
public sealed record PenetrationDeploymentResult(string RuntimePublicId, bool Reused);
public sealed record PenetrationSceneCounts(int Total, int Pending, int Provisioning, int Ready, int AccessOpen, int Failed, int Draining, int Destroyed, int Paused);
public sealed record PenetrationSceneSummary(string Id, string ReleaseId, string Status, bool PreparationRequested,
    bool DesiredAccessOpen, bool DrainRequested, bool PauseRequested, PenetrationSceneCounts Counts,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record PenetrationSceneTarget(string Id, string ExternalSubject, string DisplayName, string RuntimeId,
    string Status, string? OperationId, TeamLabRuntimeStatus RuntimeStatus, string RuntimeStage,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Error);
public sealed record PenetrationTargetPage(IReadOnlyList<PenetrationSceneTarget> Items, string? NextCursor);
