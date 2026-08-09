using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabRemoteAccessAuthorizationProvider
{
    Task<TeamLabOperatorPermission> GetRemoteAccessPermissionsAsync(
        int runtimeId,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
