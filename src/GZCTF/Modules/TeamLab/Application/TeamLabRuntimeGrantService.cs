using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeGrantService(AppDbContext context, TeamLabAuthorizationService authorization)
{
    public async Task<IReadOnlyList<TeamLabRuntimeGrantModel>> ListAsync(Guid runtimeId, Guid actorUserId,
        Guid? apiTokenId, bool administrator, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorUserId, apiTokenId, administrator, null,
            TeamLabRuntimePermission.RuntimeManage, token);
        return await ProjectAsync(runtimeId, token);
    }

    public async Task<IReadOnlyList<TeamLabRuntimeGrantModel>> ReplaceAsync(Guid runtimeId,
        ReplaceTeamLabRuntimeGrantsModel command, Guid actorUserId, Guid? apiTokenId, bool administrator,
        CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorUserId, apiTokenId, administrator, null,
            TeamLabRuntimePermission.RuntimeManage, token);
        var runtime = await context.TeamLabRuntimes.Include(item => item.Grants).Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        var grants = command.Grants.Select(item => new
        {
            SubjectType = item.SubjectType.Trim().ToLowerInvariant(),
            item.SubjectId,
            AssetKey = string.IsNullOrWhiteSpace(item.AssetKey) ? null : item.AssetKey.Trim(),
            Permissions = TeamLabRuntimePermissionCodec.Parse(item.Permissions)
        }).ToArray();
        if (grants.Any(item => item.SubjectType is not ("user" or "apitoken")))
            throw new TeamLabApiContractException("runtime_grant_subject_invalid", "授权主体只能是 user 或 apiToken", 422);
        if (grants.Any(item => item.Permissions == TeamLabRuntimePermission.None))
            throw new TeamLabApiContractException("runtime_grant_permission_invalid", "每条授权至少需要一个权限", 422);
        if (grants.GroupBy(item => (item.SubjectType, item.SubjectId, item.AssetKey)).Any(group => group.Count() > 1))
            throw new TeamLabApiContractException("runtime_grant_duplicated", "同一主体和资产范围只能配置一次", 422);
        var assetKeys = runtime.Assets
            .Where(item => item.Generation == runtime.Generation && item.Status != TeamLabRuntimeStatus.Destroyed)
            .Select(item => item.TopologyKey).ToHashSet(StringComparer.Ordinal);
        if (grants.Any(item => item.AssetKey is not null && !assetKeys.Contains(item.AssetKey)))
            throw new TeamLabApiContractException("runtime_grant_asset_not_found", "授权资产不属于当前运行环境", 422);
        var userIds = grants.Where(item => item.SubjectType == "user").Select(item => item.SubjectId).Distinct().ToArray();
        var tokenIds = grants.Where(item => item.SubjectType == "apitoken").Select(item => item.SubjectId).Distinct().ToArray();
        if (await context.Users.CountAsync(item => userIds.Contains(item.Id), token) != userIds.Length ||
            await context.ApiTokens.CountAsync(item => tokenIds.Contains(item.Id) && item.RevokedAt == null, token) != tokenIds.Length)
            throw new TeamLabApiContractException("runtime_grant_subject_not_found", "授权用户或 API Token 不存在", 422);

        context.TeamLabRuntimeGrants.RemoveRange(runtime.Grants);
        var now = DateTimeOffset.UtcNow;
        context.TeamLabRuntimeGrants.AddRange(grants.Select(item => new TeamLabRuntimeGrant
        {
            RuntimeId = runtime.Id,
            UserId = item.SubjectType == "user" ? item.SubjectId : null,
            ApiTokenId = item.SubjectType == "apitoken" ? item.SubjectId : null,
            AssetKey = item.AssetKey,
            Permissions = (int)item.Permissions,
            GrantedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        }));
        await context.SaveChangesAsync(token);
        return await ProjectAsync(runtimeId, token);
    }

    private async Task<IReadOnlyList<TeamLabRuntimeGrantModel>> ProjectAsync(Guid runtimeId, CancellationToken token)
    {
        var rows = await context.TeamLabRuntimeGrants.AsNoTracking()
            .Where(item => item.Runtime.PublicId == runtimeId).OrderBy(item => item.Id).ToArrayAsync(token);
        var userIds = rows.Where(item => item.UserId != null).Select(item => item.UserId!.Value).ToArray();
        var tokenIds = rows.Where(item => item.ApiTokenId != null).Select(item => item.ApiTokenId!.Value).ToArray();
        var users = await context.Users.AsNoTracking().Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.UserName ?? item.Id.ToString(), token);
        var tokens = await context.ApiTokens.AsNoTracking().Where(item => tokenIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, token);
        return rows.Select(item => new TeamLabRuntimeGrantModel(
            item.Id,
            item.UserId is not null ? "user" : "apiToken",
            item.UserId ?? item.ApiTokenId!.Value,
            item.UserId is { } userId ? users.GetValueOrDefault(userId, userId.ToString()) :
                tokens.GetValueOrDefault(item.ApiTokenId!.Value, item.ApiTokenId.Value.ToString()),
            item.AssetKey,
            TeamLabRuntimePermissionCodec.Format((TeamLabRuntimePermission)item.Permissions),
            item.UpdatedAt)).ToArray();
    }
}
