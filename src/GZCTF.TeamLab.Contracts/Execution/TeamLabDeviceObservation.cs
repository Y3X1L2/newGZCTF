namespace GZCTF.TeamLab.Contracts.Execution;

public sealed record TeamLabDeviceProbeRequest(TeamLabExecutionPlanV2 Plan, string AssetKey, string ResourceId, string? NativeIdentity);
public sealed record TeamLabDeviceObservation(string Status, DateTimeOffset ObservedAt, string? ErrorCode = null,
    string? BootId = null, IReadOnlyDictionary<string, long>? ProtocolCounters = null);
