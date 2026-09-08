namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabAssetControlCommand(int Generation, string Action, string Reason, bool Confirmed = false);
public sealed record TeamLabAssetControlTask(Guid Id, string Status, string? Stage, string? ErrorCode, bool CanRetry);
public sealed record TeamLabAssetControlAvailability(bool Allowed, string? Reason);

public sealed record TeamLabAssetControlPayload(int AssetId, TeamLabAssetControlCommand Command,
    string? ResourceId, string? NativeIdentity, int Phase = 0, Guid WorkerNodeId = default);
