namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Usage projection boundary consumed by TeamLab query paths. The platform adapter
/// (currently the Penetration module) implements it; TeamLab never reads game
/// binding tables directly.
/// </summary>
public interface ITeamLabUsageProjectionProvider
{
    Task<IReadOnlyDictionary<int, int>> GetGameBindingCountsAsync(
        IReadOnlyList<int> topologyIds,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<int>> GetGameBoundRuntimeIdsAsync(CancellationToken cancellationToken);
}
