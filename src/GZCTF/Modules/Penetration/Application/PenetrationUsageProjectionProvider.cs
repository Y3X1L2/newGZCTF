using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

/// <summary>
/// Platform adapter implementation of the TeamLab usage projection boundary.
/// It maps Penetration bindings into the generic shape TeamLab consumes, so
/// TeamLab has no direct reference to game binding tables.
/// </summary>
public sealed class PenetrationUsageProjectionProvider(AppDbContext context) : ITeamLabUsageProjectionProvider
{
    public async Task<IReadOnlyDictionary<int, int>> GetGameBindingCountsAsync(
        IReadOnlyList<int> topologyIds,
        CancellationToken cancellationToken)
    {
        if (topologyIds.Count == 0) return new Dictionary<int, int>();
        return await context.PenetrationGameLabBindings.AsNoTracking()
            .Where(item => topologyIds.Contains(item.TopologyId))
            .GroupBy(item => item.TopologyId)
            .Select(group => new { TopologyId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.TopologyId, item => item.Count, cancellationToken);
    }

    public async Task<IReadOnlySet<int>> GetGameBoundRuntimeIdsAsync(CancellationToken cancellationToken) =>
        (await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Select(item => item.RuntimeId)
            .Distinct()
            .ToArrayAsync(cancellationToken)).ToHashSet();
}
