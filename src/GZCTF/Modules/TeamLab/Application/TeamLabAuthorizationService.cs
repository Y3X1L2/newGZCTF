using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// The four independently evaluated TeamLab operator permission levels. A grant
/// covers only its named action; remote-session permission never implies lifecycle
/// control, and read permission never implies remote session operation.
/// </summary>
[Flags]
public enum TeamLabRuntimePermission
{
    None = 0,
    StateRead = 1,
    MetadataRead = 2,
    RemoteSessionOperate = 4,
    LifecycleManage = 8
}

public interface ITeamLabRuntimeManagerAuthorizationProvider
{
    Task<bool> CanManageRuntimeAsync(
        int runtimeId,
        Guid actorUserId,
        CancellationToken cancellationToken);
}

public sealed class TeamLabAuthorizationService(
    AppDbContext context,
    IEnumerable<ITeamLabRuntimeManagerAuthorizationProvider> managerProviders,
    IEnumerable<ITeamLabRemoteAccessAuthorizationProvider> remoteAccessProviders)
{
    public async Task RequireRuntimeOwnerAsync(
        Guid runtimeId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var owner = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => item.CreatedById)
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null)
            throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (!administrator && owner != actorUserId)
            throw new TeamLabApiContractException("insufficient_permission", "该运行时不属于当前操作者", 403);
    }

    public async Task RequireRuntimeManagerAsync(
        Guid runtimeId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken) =>
        await RequirePermissionAsync(runtimeId, actorUserId, administrator,
            TeamLabRuntimePermission.LifecycleManage, cancellationToken);

    /// <summary>
    /// Evaluates the four permission levels through one boundary. Owner and
    /// administrator carry all levels; manager providers grant lifecycle control
    /// plus the read levels; remote-session providers grant session operation plus
    /// state read but never lifecycle control.
    /// </summary>
    public async Task<TeamLabRuntimePermission> EvaluateAsync(
        Guid runtimePublicId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimePublicId)
            .Select(item => new { item.Id, item.CreatedById })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);

        if (administrator || runtime.CreatedById == actorUserId)
            return TeamLabRuntimePermission.StateRead | TeamLabRuntimePermission.MetadataRead |
                   TeamLabRuntimePermission.RemoteSessionOperate | TeamLabRuntimePermission.LifecycleManage;

        var permissions = TeamLabRuntimePermission.None;
        foreach (var provider in managerProviders)
        {
            if (await provider.CanManageRuntimeAsync(runtime.Id, actorUserId, cancellationToken))
            {
                permissions |= TeamLabRuntimePermission.StateRead |
                               TeamLabRuntimePermission.MetadataRead |
                               TeamLabRuntimePermission.LifecycleManage;
                break;
            }
        }

        var remote = TeamLabRuntimePermission.None;
        foreach (var provider in remoteAccessProviders)
        {
            var granted = await provider.GetRemoteAccessPermissionsAsync(runtime.Id, actorUserId, cancellationToken);
            if ((granted & TeamLabOperatorPermission.ViewAssets) != 0)
                remote |= TeamLabRuntimePermission.StateRead;
            if ((granted & TeamLabOperatorPermission.OperateAssets) != 0)
                remote |= TeamLabRuntimePermission.RemoteSessionOperate;
        }
        permissions |= remote;
        return permissions;
    }

    public async Task RequirePermissionAsync(
        Guid runtimePublicId,
        Guid actorUserId,
        bool administrator,
        TeamLabRuntimePermission required,
        CancellationToken cancellationToken)
    {
        var permissions = await EvaluateAsync(runtimePublicId, actorUserId, administrator, cancellationToken);
        if ((permissions & required) == required) return;
        throw new TeamLabApiContractException(
            "insufficient_permission",
            "操作者不具备该 TeamLab 运行时操作权限",
            403);
    }
}
