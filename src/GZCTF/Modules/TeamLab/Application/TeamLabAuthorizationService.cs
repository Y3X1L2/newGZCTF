using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

[Flags]
public enum TeamLabRuntimePermission
{
    None = 0,
    StateRead = 1 << 0,
    MetadataRead = 1 << 1,
    RemoteSessionOperate = 1 << 2,
    FileTransfer = 1 << 3,
    AssetOperate = 1 << 4,
    AssetCompose = 1 << 5,
    ServiceAccessManage = 1 << 6,
    RuntimeManage = 1 << 7,
    All = StateRead | MetadataRead | RemoteSessionOperate | FileTransfer | AssetOperate |
          AssetCompose | ServiceAccessManage | RuntimeManage
}

public static class TeamLabRuntimePermissionCodec
{
    private static readonly IReadOnlyDictionary<string, TeamLabRuntimePermission> Values =
        new Dictionary<string, TeamLabRuntimePermission>(StringComparer.OrdinalIgnoreCase)
        {
            ["StateRead"] = TeamLabRuntimePermission.StateRead,
            ["MetadataRead"] = TeamLabRuntimePermission.MetadataRead,
            ["RemoteSessionOperate"] = TeamLabRuntimePermission.RemoteSessionOperate,
            ["FileTransfer"] = TeamLabRuntimePermission.FileTransfer,
            ["AssetOperate"] = TeamLabRuntimePermission.AssetOperate,
            ["AssetCompose"] = TeamLabRuntimePermission.AssetCompose,
            ["ServiceAccessManage"] = TeamLabRuntimePermission.ServiceAccessManage,
            ["RuntimeManage"] = TeamLabRuntimePermission.RuntimeManage
        };

    public static TeamLabRuntimePermission Parse(IEnumerable<string> values)
    {
        var permissions = TeamLabRuntimePermission.None;
        foreach (var value in values)
            permissions |= Values.TryGetValue(value.Trim(), out var permission)
                ? permission
                : throw new TeamLabApiContractException(
                    "runtime_grant_permission_invalid", $"未知的运行权限：{value}", 422);
        return permissions;
    }

    public static string[] Format(TeamLabRuntimePermission permissions) => Values
        .Where(item => permissions.HasFlag(item.Value))
        .Select(item => item.Key)
        .ToArray();
}

public sealed class TeamLabAuthorizationService(AppDbContext context)
{
    public async Task<Guid?> GetControlScopeAsync(Guid runtimeId, CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => new { item.ControlScopeId })
            .SingleOrDefaultAsync(token);
        if (runtime is null)
            throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        return runtime.ControlScopeId;
    }

    public async Task RequireRuntimeOwnerAsync(Guid runtimeId, Guid actorUserId, bool administrator,
        CancellationToken token)
    {
        var owner = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => item.CreatedById)
            .SingleOrDefaultAsync(token);
        if (owner is null)
            throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (!administrator && owner != actorUserId)
            throw new TeamLabApiContractException("insufficient_permission", "该运行时不属于当前操作者", 403);
    }

    public Task RequireRuntimeManagerAsync(Guid runtimeId, Guid actorUserId, bool administrator,
        CancellationToken token) => RequirePermissionAsync(runtimeId, actorUserId, administrator,
        TeamLabRuntimePermission.RuntimeManage, token);

    public Task<TeamLabRuntimePermission> EvaluateAsync(Guid runtimeId, Guid actorUserId, bool administrator,
        CancellationToken token) => EvaluateAsync(runtimeId, actorUserId, null, administrator, null, token);

    public async Task<TeamLabRuntimePermission> EvaluateAsync(
        Guid runtimeId,
        Guid actorUserId,
        Guid? apiTokenId,
        bool administrator,
        string? assetKey,
        CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => new { item.Id, item.CreatedById, item.ControlScopeId })
            .SingleOrDefaultAsync(token)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (administrator || apiTokenId is null && runtime.CreatedById == actorUserId)
            return TeamLabRuntimePermission.All;
        if (apiTokenId is { } tokenId && runtime.ControlScopeId is { } scopeId &&
            await context.ApiTokenResourceGrants.AsNoTracking().AnyAsync(grant =>
                grant.TokenId == tokenId && grant.ResourceType == "teamlab-scope" &&
                (grant.ResourceId == scopeId.ToString() || grant.ResourceId == "*"), token))
            return TeamLabRuntimePermission.All;

        var values = await context.TeamLabRuntimeGrants.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id &&
                           (apiTokenId == null ? item.UserId == actorUserId : item.ApiTokenId == apiTokenId) &&
                           (item.AssetKey == null || assetKey != null && item.AssetKey == assetKey))
            .Select(item => item.Permissions)
            .ToArrayAsync(token);
        return values.Aggregate(TeamLabRuntimePermission.None,
            (current, value) => current | (TeamLabRuntimePermission)value);
    }

    public Task RequirePermissionAsync(Guid runtimeId, Guid actorUserId, bool administrator,
        TeamLabRuntimePermission required, CancellationToken token) =>
        RequirePermissionAsync(runtimeId, actorUserId, null, administrator, null, required, token);

    public async Task RequirePermissionAsync(Guid runtimeId, Guid actorUserId, Guid? apiTokenId,
        bool administrator, string? assetKey, TeamLabRuntimePermission required, CancellationToken token)
    {
        var permissions = await EvaluateAsync(runtimeId, actorUserId, apiTokenId, administrator, assetKey, token);
        if ((permissions & required) == required) return;
        throw new TeamLabApiContractException(
            "insufficient_permission", "操作者不具备该 TeamLab 运行时操作权限", 403);
    }

    public async Task RequireAssetPermissionAsync(Guid runtimeId, int assetId, Guid actorUserId,
        Guid? apiTokenId, bool administrator, TeamLabRuntimePermission required, CancellationToken token)
    {
        var assetKey = await context.TeamLabRuntimeAssets.AsNoTracking()
            .Where(item => item.Id == assetId && item.Runtime.PublicId == runtimeId &&
                           item.Generation == item.Runtime.Generation)
            .Select(item => item.TopologyKey)
            .SingleOrDefaultAsync(token)
            ?? throw new TeamLabApiContractException("runtime_asset_not_found", "未找到运行资产", 404);
        await RequirePermissionAsync(runtimeId, actorUserId, apiTokenId, administrator, assetKey, required, token);
    }

    public async Task<IReadOnlySet<string>?> GetAssetScopeAsync(
        Guid runtimeId,
        Guid actorUserId,
        Guid? apiTokenId,
        bool administrator,
        TeamLabRuntimePermission required,
        CancellationToken token)
    {
        if ((await EvaluateAsync(runtimeId, actorUserId, apiTokenId, administrator, null, token) & required) == required)
            return null;
        var keys = await context.TeamLabRuntimeGrants.AsNoTracking()
            .Where(item => item.Runtime.PublicId == runtimeId && item.AssetKey != null &&
                           (apiTokenId == null ? item.UserId == actorUserId : item.ApiTokenId == apiTokenId) &&
                           (item.Permissions & (int)required) == (int)required)
            .Select(item => item.AssetKey!)
            .Distinct()
            .ToArrayAsync(token);
        if (keys.Length == 0)
            throw new TeamLabApiContractException(
                "insufficient_permission", "操作者不具备该 TeamLab 运行时操作权限", 403);
        return keys.ToHashSet(StringComparer.Ordinal);
    }
}
