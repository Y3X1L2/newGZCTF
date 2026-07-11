using GZCTF.Models;
using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Ctf.Infrastructure;

public sealed class GameApiTokenResourceGrantPolicy(AppDbContext context) : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "game";

    public Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (actor.Role >= Role.Admin)
            return Task.FromResult(resourceId == "*" || int.TryParse(resourceId, out _));
        if (actor.UserId is not { } actorUserId ||
            !int.TryParse(resourceId, out var gameId) || gameId <= 0)
            return Task.FromResult(false);
        return context.Games.AsNoTracking().AnyAsync(
            game => game.Id == gameId && game.OwnerId == actorUserId,
            cancellationToken);
    }
}
