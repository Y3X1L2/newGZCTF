using GZCTF.Models;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

/// <summary>
/// Resolves TeamLab operator grants without constructing the deployment adapter.
/// Remote-access authorization is on the request path and must not depend on
/// runtime orchestration services.
/// </summary>
public sealed class PenetrationTeamLabRemoteAccessAuthorizationProvider(AppDbContext context)
    : ITeamLabRemoteAccessAuthorizationProvider, ITeamLabRuntimeManagerAuthorizationProvider
{
    public Task<bool> CanManageRuntimeAsync(
        int runtimeId,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.RuntimeId == runtimeId)
            .Join(
                context.Games.AsNoTracking(),
                binding => binding.GameId,
                game => game.Id,
                (_, game) => game.OwnerId)
            .AnyAsync(ownerId => ownerId == actorUserId, cancellationToken);

    public async Task<TeamLabOperatorPermission> GetRemoteAccessPermissionsAsync(
        int runtimeId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var gameId = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.RuntimeId == runtimeId)
            .Select(item => (int?)item.GameId)
            .SingleOrDefaultAsync(cancellationToken);
        if (gameId is null) return TeamLabOperatorPermission.None;

        var owner = await context.Games.AsNoTracking()
            .Where(item => item.Id == gameId.Value)
            .Select(item => item.OwnerId == actorUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (owner)
            return TeamLabOperatorPermission.ViewAssets | TeamLabOperatorPermission.OperateAssets;

        return await context.PenetrationTeamLabOperatorGrants.AsNoTracking()
            .Where(item => item.GameId == gameId.Value && item.UserId == actorUserId)
            .Select(item => (TeamLabOperatorPermission?)item.Permissions)
            .SingleOrDefaultAsync(cancellationToken)
            ?? TeamLabOperatorPermission.None;
    }
}
