using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class EfTeamLabReleaseCatalog(AppDbContext db) : ITeamLabReleaseCatalog
{
    public Task<bool> IsAvailableAsync(Guid topologyId, Guid releaseId, CancellationToken cancellationToken) =>
        db.TeamLabTopologyReleases.AsNoTracking().AnyAsync(x => x.Id == releaseId &&
            x.Topology.PublicId == topologyId && !x.IsArchived, cancellationToken);
}
