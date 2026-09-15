namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record CreateTeamLabServiceAccessModel(
    string Protocol, int InternalPort, int? PublicPort = null, string? NetworkKey = null);

public sealed record TeamLabServiceAccessModel(
    Guid Id, Guid RuntimeId, int Generation, int AssetId, string AssetName, string NetworkKey,
    string Protocol, int InternalPort, int PublicPort, string Endpoint, string Status,
    string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);
