using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeLifecycleGuard(AppDbContext context)
{
    public Task<bool> IsRolloutManagedAsync(Guid runtimePublicId, CancellationToken cancellationToken) =>
        context.TeamLabRolloutTargets.AsNoTracking().AnyAsync(
            target => target.Runtime != null && target.Runtime.PublicId == runtimePublicId,
            cancellationToken);
}
