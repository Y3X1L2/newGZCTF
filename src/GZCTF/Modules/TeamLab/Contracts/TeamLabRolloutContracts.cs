using GZCTF.Models.Data;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabRolloutCountsModel(
    int Total,
    int Pending,
    int Provisioning,
    int Ready,
    int AccessOpen,
    int Failed,
    int Draining,
    int Destroyed);

public sealed record TeamLabRolloutModel(
    Guid Id,
    Guid ReleaseId,
    string Status,
    bool PreparationRequested,
    bool DesiredAccessOpen,
    bool DrainRequested,
    TeamLabRolloutCountsModel Counts,
    DateTimeOffset? PreparedAt,
    DateTimeOffset? AccessOpenedAt,
    DateTimeOffset? DrainingAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error);

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
