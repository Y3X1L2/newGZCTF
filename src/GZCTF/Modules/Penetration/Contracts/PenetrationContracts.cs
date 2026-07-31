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
    Guid TopologyId,
    Guid? ActiveReleaseId,
    int MaxResetCount,
    long ObjectiveRevision,
    IReadOnlyList<PenetrationObjectiveModel> Objectives);

public sealed record PenetrationReleaseOptionModel(
    Guid TopologyId,
    string TopologyName,
    Guid ReleaseId,
    int Version,
    int NetworkCount,
    int AssetCount,
    DateTimeOffset PublishedAt);

public sealed record PenetrationGameTeamLabModel(
    PenetrationGameLabBindingModel? Binding,
    TeamLabRolloutModel? Rollout);

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
    Guid RuntimeId,
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
    Guid RuntimeId,
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
    Guid RuntimeId,
    DateTimeOffset Time);

public sealed record TeamLabOperatorGrantWriteModel(bool ViewAssets, bool OperateAssets);

public sealed record TeamLabOperatorGrantModel(
    Guid UserId,
    string UserName,
    string? DisplayName,
    bool ViewAssets,
    bool OperateAssets,
    DateTimeOffset UpdatedAt);

