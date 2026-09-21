using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;

namespace GZCTF.Modules.League.Infrastructure;

// Explicitly unavailable defaults. Successful test doubles belong exclusively in test projects.
public sealed class UnavailableLeagueProviders : ILeagueRuntimePort, ILeagueFlagPort, ILeagueCoinPort
{
    public bool IsAvailable => false;
    private static LeagueException Unavailable() => new("league_dependency_unavailable", "联赛提供者尚未接入。", 503);
    public Task<LeaguePreparationResult> PrepareAsync(LeagueFrozenMatch match, IReadOnlyList<LeagueCoreReference> cores, CancellationToken cancellationToken) => throw Unavailable();
    public Task<LeagueEffectResult> OpenAccessAsync(LeagueFrozenMatch match, Guid operationId, IReadOnlyList<LeagueRuntimeBinding> bindings, CancellationToken cancellationToken) => throw Unavailable();
    public Task<LeagueEffectResult> CleanupAsync(LeagueFrozenMatch match, Guid operationId, CancellationToken cancellationToken) => throw Unavailable();
    public Task<IReadOnlyList<LeagueCoreReference>> PrepareAsync(LeagueFrozenMatch match, CancellationToken cancellationToken) => throw Unavailable();
    public Task<bool> ConfirmBindingsAsync(LeagueFrozenMatch match, IReadOnlyList<LeagueRuntimeBinding> bindings, CancellationToken cancellationToken) => throw Unavailable();
    public Task InitializeAsync(LeagueFrozenMatch match, CancellationToken cancellationToken) => throw Unavailable();
}
