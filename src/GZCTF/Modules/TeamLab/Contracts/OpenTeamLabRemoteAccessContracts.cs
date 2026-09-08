using System.ComponentModel.DataAnnotations;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record OpenCreateTeamLabRemoteSessionModel(
    [property: Required, StringLength(500, MinimumLength = 4)] string Reason, bool VncConsole = false);

public sealed record OpenTeamLabRemoteSessionModel(
    Guid Id, Guid RuntimeId, int AssetId, string AssetName,
    TeamLabRemoteProtocol Protocol, TeamLabRemoteSessionStatus Status,
    string Reason, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? ConnectedAt, DateTimeOffset? EndedAt, string? EndReason);

public sealed record OpenTeamLabRemoteAvailabilityModel(
    int AssetId, string AssetName, TeamLabRemoteProtocol? Protocol, bool Available, string? UnavailableReason);

public sealed record OpenTeamLabRemoteConnectModel(string Url, DateTimeOffset ExpiresAt);

public static class OpenTeamLabRemoteAccessMapping
{
    public static OpenTeamLabRemoteSessionModel ToOpen(this TeamLabRemoteSessionModel session) => new(
        session.Id, session.RuntimeId, session.AssetId, session.AssetName, session.Protocol, session.Status,
        session.Reason, session.CreatedAt, session.ExpiresAt, session.ConnectedAt, session.EndedAt, session.EndReason);

    public static OpenTeamLabRemoteAvailabilityModel ToOpen(this TeamLabRemoteAccessAvailabilityModel item) =>
        new(item.AssetId, item.AssetName, item.Protocol, item.Available, item.UnavailableReason);
}
