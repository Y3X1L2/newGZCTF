namespace GZCTF.TeamLab.Contracts.Execution;

public sealed record TeamLabExecutionEventV2(
    int RuntimeId,
    Guid RuntimePublicId,
    int Generation,
    string ShardKey,
    string? AssetKey,
    string Stage,
    string Outcome,
    string? ErrorCategory,
    string? ErrorCode,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string>? Detail = null);

public sealed record TeamLabExecutionPlanApplyRequest(TeamLabExecutionPlanV2 Plan);

public sealed record TeamLabExecutionPlanCleanupRequest(TeamLabExecutionPlanV2 Plan);

public sealed record TeamLabExecutionPlanCleanupResponse(
    bool Success,
    string PlanDigest,
    IReadOnlyList<TeamLabExecutionEventV2> Events,
    IReadOnlyList<TeamLabExecutionInventoryFactV2> Inventory,
    string? ErrorCategory = null,
    string? ErrorCode = null,
    string? Message = null);

public sealed record TeamLabExecutionPlanApplyResponse(
    bool Success,
    bool AlreadyApplied,
    string PlanDigest,
    IReadOnlyList<TeamLabExecutionEventV2> Events,
    IReadOnlyList<TeamLabExecutionInventoryFactV2> Inventory,
    string? ErrorCategory = null,
    string? ErrorCode = null,
    string? Message = null);

public sealed record TeamLabExecutionInventoryFactV2(
    string Kind,
    string AssetKey,
    string ResourceId,
    string State,
    int Generation);
