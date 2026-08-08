namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Default no-op usage projection used when no platform adapter is registered.
/// The Penetration module overrides this registration with its binding-backed adapter.
/// </summary>
public sealed class TeamLabEmptyUsageProjectionProvider : ITeamLabUsageProjectionProvider
{
    public Task<IReadOnlyDictionary<int, int>> GetGameBindingCountsAsync(
        IReadOnlyList<int> topologyIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int>());

    public Task<IReadOnlySet<int>> GetGameBoundRuntimeIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
}
