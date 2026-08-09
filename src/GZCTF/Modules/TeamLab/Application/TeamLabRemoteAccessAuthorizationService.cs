using GZCTF.Models;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRemoteAccessAuthorizationService(
    AppDbContext context,
    IEnumerable<ITeamLabRemoteAccessAuthorizationProvider> providers)
{
    public async Task<TeamLabOperatorPermission> GetPermissionsAsync(
        Guid runtimePublicId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimePublicId)
            .Select(item => new { item.Id, item.CreatedById })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);

        if (administrator || runtime.CreatedById == actorUserId)
            return TeamLabOperatorPermission.ViewAssets | TeamLabOperatorPermission.OperateAssets;

        var permissions = TeamLabOperatorPermission.None;
        foreach (var provider in providers)
            permissions |= await provider.GetRemoteAccessPermissionsAsync(runtime.Id, actorUserId, cancellationToken);
        return permissions;
    }

    public async Task RequireAsync(
        Guid runtimePublicId,
        Guid actorUserId,
        bool administrator,
        TeamLabOperatorPermission required,
        CancellationToken cancellationToken)
    {
        var permissions = await GetPermissionsAsync(runtimePublicId, actorUserId, administrator, cancellationToken);
        if ((permissions & required) == required) return;
        throw new TeamLabApiContractException(
            "insufficient_remote_access_permission",
            "The operation actor is not authorized to access TeamLab runtime assets.",
            403);
    }
}
