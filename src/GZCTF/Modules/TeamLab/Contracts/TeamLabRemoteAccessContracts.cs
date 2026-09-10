using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record CreateTeamLabRemoteSessionModel(string Reason, bool VncConsole = false);

public sealed record TeamLabRemoteAccessAvailabilityModel(
    int AssetId,
    string AssetName,
    TeamLabRemoteProtocol? Protocol,
    bool Available,
    string? UnavailableReason);

public sealed record TeamLabRemoteSessionModel(
    Guid Id,
    Guid RuntimeId,
    int AssetId,
    string AssetName,
    TeamLabRemoteProtocol Protocol,
    TeamLabRemoteSessionStatus Status,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? EndedAt,
    string? EndReason);

public sealed record TeamLabRemoteConnectModel(string Url, DateTimeOffset ExpiresAt);

public sealed record TeamLabRemoteSessionListItem(
    TeamLabRemoteSessionModel Session, Guid WorkerNodeId, Guid RequestedByUserId);

public sealed record TeamLabRemoteSessionPage(IReadOnlyList<TeamLabRemoteSessionListItem> Items, long? NextCursor);
