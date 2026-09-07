using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class AssetApiTokenResourceGrantPolicy(AssetApplicationService assets) : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "asset";

    public Task<bool> CanGrantAsync(ActorContext actor, string resourceId, CancellationToken cancellationToken)
    {
        if (actor.UserId is not { } actorId || actor.Role < Role.Teacher)
            return Task.FromResult(false);
        if (actor.Role >= Role.Admin)
            return Task.FromResult(resourceId == "*" || AssetApplicationService.IsValidHash(resourceId));
        return assets.CanAccessAsync(actorId, resourceId, cancellationToken);
    }
}
