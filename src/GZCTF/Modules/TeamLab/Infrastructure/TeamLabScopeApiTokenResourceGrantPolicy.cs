using GZCTF.Models;
using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabScopeApiTokenResourceGrantPolicy(AppDbContext context) : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "teamlab-scope";

    public Task<bool> CanGrantAsync(ActorContext actor, string resourceId, CancellationToken cancellationToken)
    {
        if (actor.Role < Role.Admin)
            return Task.FromResult(false);

        if (resourceId == "*")
            return Task.FromResult(true);

        if (!Guid.TryParse(resourceId, out var scopeId))
            return Task.FromResult(false);

        return context.TeamLabControlScopes.AsNoTracking()
            .AnyAsync(scope => scope.Id == scopeId && !scope.IsArchived, cancellationToken);
    }
}
