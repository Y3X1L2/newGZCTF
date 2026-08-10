using GZCTF.Models.Data;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabRolloutTargetInputModel(
    string ExternalSubject,
    string DisplayName);

public sealed record CreateTeamLabRolloutModel(
    Guid ControlScopeId,
    Guid ReleaseId,
    string ExternalReference,
    IReadOnlyList<TeamLabRolloutTargetInputModel> Targets);

public sealed record ReplaceTeamLabRolloutTargetsModel(
    IReadOnlyList<TeamLabRolloutTargetInputModel> Targets);

public sealed record TeamLabRolloutCountsModel(
    int Total,
    int Pending,
    int Provisioning,
    int Ready,
    int AccessOpen,
    int Failed,
    int Draining,
    int Destroyed,
    int Paused);

public sealed record TeamLabRolloutModel(
    Guid Id,
    Guid ReleaseId,
    string Status,
    bool PreparationRequested,
    bool DesiredAccessOpen,
    bool DrainRequested,
    bool PauseRequested,
    TeamLabRolloutCountsModel Counts,
    DateTimeOffset? PreparedAt,
    DateTimeOffset? AccessOpenedAt,
    DateTimeOffset? DrainingAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error,
    Guid? ControlScopeId = null,
    string? AdapterKind = null,
    string? ExternalReference = null,
    int Revision = 0);

public sealed record TeamLabRolloutPageModel(
    IReadOnlyList<TeamLabRolloutModel> Items,
    string? NextCursor);

public sealed record TeamLabRolloutTargetModel(
    Guid Id,
    string ExternalSubject,
    string DisplayName,
    Guid? RuntimeId,
    string Status,
    Guid? OperationId,
    TeamLabRuntimeStatus? RuntimeStatus,
    string? RuntimeStage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error);

public sealed record TeamLabRolloutTargetPageModel(
    IReadOnlyList<TeamLabRolloutTargetModel> Items,
    string? NextCursor);
