namespace GZCTF.TeamLab.Contracts.Execution;

public sealed record TeamLabAssetControlRequest(TeamLabExecutionPlanV2 Plan, string AssetKey,
    string Action, string? ExpectedResourceId, string? ExpectedNativeIdentity);

public sealed record TeamLabAssetControlResult(bool Success, string? ErrorCode,
    TeamLabExecutionInventoryFactV2? Asset);
