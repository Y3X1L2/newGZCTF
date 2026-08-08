namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabControlScopeModel(
    Guid Id,
    string Key,
    string DisplayName,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTeamLabControlScopeModel(string Key, string DisplayName);
