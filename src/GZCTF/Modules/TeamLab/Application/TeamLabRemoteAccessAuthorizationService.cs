using GZCTF.Models;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Remote-access permission gate. All decisions are delegated to the unified
/// four-level <see cref="TeamLabAuthorizationService"/> evaluation so the
/// permission projection stays coherent across browser, token and provider paths.
/// </summary>
public sealed class TeamLabRemoteAccessAuthorizationService(
    TeamLabAuthorizationService authorization)
{
    public async Task<TeamLabOperatorPermission> GetPermissionsAsync(
        Guid runtimePublicId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var permissions = await authorization.EvaluateAsync(
            runtimePublicId, actorUserId, administrator, cancellationToken);
        var result = TeamLabOperatorPermission.None;
        if ((permissions & TeamLabRuntimePermission.StateRead) != 0)
            result |= TeamLabOperatorPermission.ViewAssets;
        if ((permissions & TeamLabRuntimePermission.RemoteSessionOperate) != 0)
            result |= TeamLabOperatorPermission.OperateAssets;
        return result;
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
            "操作者无权访问 TeamLab 运行时资源",
            403);
    }
}
