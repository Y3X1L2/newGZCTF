using System.ComponentModel.DataAnnotations;

namespace GZCTF.Modules.League.Contracts;

public enum LeagueMatchState { Draft, Preparing, Ready, Starting, Running, Ended }
public enum LeagueRegistrationState { Pending, Approved, Rejected }
public enum LeagueProgressState { Pending, Running, Ready, Failed }
public enum LeagueEndReason { Knockout, Aborted }
public enum LeagueFailure { None, DependencyUnavailable, EnvironmentFailed, FlagBindingFailed, AccessFailed, CleanupFailed, ProviderUnavailable, InvalidProviderResponse }

public sealed record LeagueDraftModel(
    [Required, StringLength(160, MinimumLength = 1)] string Name,
    Guid? TopologyId, Guid? ReleaseId,
    [Range(0, int.MaxValue)] int InitialCoins);
public sealed record LeagueUpdateDraftModel(int Revision, LeagueDraftModel Draft);
public sealed record LeagueReviewModel(LeagueRegistrationState State);
public sealed record LeagueSelectTeamsModel(int Revision, int FirstTeamId, int SecondTeamId);
public sealed record LeagueRevisionModel(int Revision);
public sealed record LeagueRegisterModel(int TeamId);
public sealed record LeagueAbortModel([Required, StringLength(500, MinimumLength = 1)] string Reason);

public sealed record LeagueTeamSnapshot(int TeamId, string Name, int Seat, IReadOnlyList<Guid> MemberIds);
public sealed record LeagueFrozenMatch(Guid MatchId, int ConfigurationVersion, Guid PreparationId,
    Guid TopologyId, Guid ReleaseId, int InitialCoins, IReadOnlyList<LeagueTeamSnapshot> Teams);
public sealed record LeagueRuntimeBinding(int TeamId, Guid RuntimeId, int Generation);

/// <summary>Opaque reference only. Flag plaintext must never enter match storage or HTTP projections.</summary>
public sealed record LeagueCoreReference(int TeamId, string CoreKey, Guid MaterialId);
public sealed record LeagueTeamPreparation(int TeamId, LeagueProgressState State,
    LeagueRuntimeBinding? Binding, bool EnvironmentReady, bool FlagInjected, bool EntryPrepared,
    bool AccessClosed, LeagueFailure Failure = LeagueFailure.None, bool Retryable = true);
public sealed record LeaguePreparationResult(Guid OperationId, IReadOnlyList<LeagueTeamPreparation> Teams);
public sealed record LeagueEffectResult(bool Completed, LeagueFailure Failure = LeagueFailure.None, bool Retryable = true);

public sealed record LeagueRegistrationModel(int TeamId, string TeamName, LeagueRegistrationState State,
    bool Selected, int? Seat, IReadOnlyList<Guid> MemberIds);
public sealed record LeagueOperationModel(Guid Id, LeagueProgressState State, LeagueFailure Failure,
    bool Retryable, int AttemptCount);
public sealed record LeagueResultModel(int? WinnerTeamId, LeagueEndReason Reason, Guid? SubmissionId, DateTimeOffset EndedAt, string? AbortReason);
public sealed record LeagueMatchSummary(Guid Id, string Name, LeagueMatchState State, int Revision,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, LeagueResultModel? Result);
public sealed record LeagueMatchDetail(LeagueMatchSummary Match, Guid? TopologyId, Guid? ReleaseId,
    int InitialCoins, int? ConfigurationVersion, IReadOnlyList<LeagueRegistrationModel> Registrations,
    IReadOnlyList<LeagueTeamPreparation> Preparation, LeagueOperationModel? Operation,
    LeagueOperationModel? Cleanup, IReadOnlyList<string> AllowedActions);
public sealed record LeagueMatchPage(IReadOnlyList<LeagueMatchSummary> Items, Guid? NextCursor);

/// <summary>yhr: calls are idempotent by caller operation ID; reuse DeploymentQueueTicket for execution.</summary>
public interface ILeagueRuntimePort
{
    bool IsAvailable { get; }
    Task<LeaguePreparationResult> PrepareAsync(LeagueFrozenMatch match, IReadOnlyList<LeagueCoreReference> cores,
        CancellationToken cancellationToken);
    // Completion means both teams have their allowed access; partial failure stays Starting.
    Task<LeagueEffectResult> OpenAccessAsync(LeagueFrozenMatch match, Guid operationId,
        IReadOnlyList<LeagueRuntimeBinding> bindings, CancellationToken cancellationToken);
    // Must fence in-flight preparation/open requests, close access, and reclaim even unreported resources.
    Task<LeagueEffectResult> CleanupAsync(LeagueFrozenMatch match, Guid operationId, CancellationToken cancellationToken);
}

/// <summary>lmr: stable references per preparation/team/core, then validate actual runtime generation bindings.</summary>
public interface ILeagueFlagPort
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<LeagueCoreReference>> PrepareAsync(LeagueFrozenMatch match, CancellationToken cancellationToken);
    Task<bool> ConfirmBindingsAsync(LeagueFrozenMatch match, IReadOnlyList<LeagueRuntimeBinding> bindings,
        CancellationToken cancellationToken);
}

/// <summary>lmr: initialize both accounts idempotently by preparation ID; amounts are nonnegative integer coins.</summary>
public interface ILeagueCoinPort
{
    bool IsAvailable { get; }
    Task InitializeAsync(LeagueFrozenMatch match, CancellationToken cancellationToken);
}

public interface ILeagueMatchQuery
{
    Task<LeagueMatchSummary> GetSummaryAsync(Guid matchId, CancellationToken cancellationToken);
    Task<LeagueFrozenMatch> GetFrozenAsync(Guid matchId, CancellationToken cancellationToken);
    Task<int?> GetParticipantTeamAsync(Guid matchId, Guid userId, CancellationToken cancellationToken);
}

public sealed record LeagueKnockoutCommand(Guid MatchId, Guid SubmissionId, Guid UserId, int WinnerTeamId,
    Guid PreparationId, LeagueRuntimeBinding Target);
public sealed record LeagueFinalizationResult(bool Applied, LeagueResultModel Result);

/// <summary>
/// lmr must begin a transaction on the same scoped AppDbContext before calling this service.
/// It locks the match, saves the result and cleanup intent, but never commits the caller transaction.
/// Validate and save the submission in that transaction; commit only after both writes succeed.
/// </summary>
public interface ILeagueFinalizationService
{
    Task<LeagueFinalizationResult> ConfirmKnockoutAsync(LeagueKnockoutCommand command, CancellationToken cancellationToken);
}
