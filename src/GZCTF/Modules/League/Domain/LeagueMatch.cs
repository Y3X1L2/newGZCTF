using GZCTF.Modules.League.Contracts;

namespace GZCTF.Modules.League.Domain;

public sealed class LeagueMatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public Guid CreatedById { get; set; }
    public LeagueMatchState State { get; set; }
    public int Revision { get; set; } = 1;
    public Guid? TopologyId { get; set; }
    public Guid? ReleaseId { get; set; }
    public int InitialCoins { get; set; }
    public int? ConfigurationVersion { get; set; }
    public Guid? PreparationId { get; set; }
    public Guid? ProviderOperationId { get; set; }
    public Guid? StartOperationId { get; set; }
    public LeagueProgressState OperationState { get; set; }
    public LeagueFailure Failure { get; set; }
    public bool Retryable { get; set; } = true;
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? CleanupOperationId { get; set; }
    public LeagueProgressState CleanupState { get; set; }
    public LeagueFailure CleanupFailure { get; set; }
    public bool CleanupRetryable { get; set; } = true;
    public int CleanupAttemptCount { get; set; }
    public int? WinnerTeamId { get; set; }
    public LeagueEndReason? EndReason { get; set; }
    public string? AbortReason { get; set; }
    public Guid? WinningSubmissionId { get; set; }
    public Guid? EndedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public List<LeagueRegistration> Registrations { get; set; } = [];
}

public sealed class LeagueRegistration
{
    public Guid MatchId { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public Guid RegisteredById { get; set; }
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public LeagueRegistrationState State { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public bool Selected { get; set; }
    public int? Seat { get; set; }
    public Guid[] MemberIds { get; set; } = [];
    public LeagueProgressState PreparationState { get; set; }
    public Guid? RuntimeId { get; set; }
    public int? Generation { get; set; }
    public bool EnvironmentReady { get; set; }
    public bool FlagInjected { get; set; }
    public bool EntryPrepared { get; set; }
    public bool AccessClosed { get; set; }
    public LeagueFailure Failure { get; set; }
    public bool Retryable { get; set; } = true;
}
