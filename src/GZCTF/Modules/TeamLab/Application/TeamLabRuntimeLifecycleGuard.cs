using GZCTF.Models;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeLifecycleGuard(AppDbContext context)
{
    public Task<bool> IsRolloutManagedAsync(Guid runtimePublicId, CancellationToken cancellationToken) =>
        context.TeamLabRolloutTargets.AsNoTracking().AnyAsync(
            target => target.Runtime != null && target.Runtime.PublicId == runtimePublicId &&
                      target.Rollout.Status != TeamLabRolloutStatus.Completed,
            cancellationToken);

    public async Task RequireRolloutTargetAsync(Guid runtimePublicId, int rolloutId, CancellationToken cancellationToken)
    {
        if (await context.TeamLabRolloutTargets.AsNoTracking().AnyAsync(
                target => target.RolloutId == rolloutId && target.Runtime != null &&
                          target.Runtime.PublicId == runtimePublicId,
                cancellationToken))
            return;

        throw new TeamLabApiContractException(
            "runtime_rollout_target_invalid",
            "该运行时不是所请求 TeamLab rollout 的目标",
            409);
    }
}
