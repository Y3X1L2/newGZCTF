using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Owns the small TeamLab resource namespace used by open API callers. This is
/// deliberately not an organization or tenant subsystem.
/// </summary>
public sealed class TeamLabControlScopeService(
    AppDbContext context,
    TeamLabReleaseImagePreparationService? imagePreparation = null)
{
    public const string PlatformScopeKey = "platform";

    public async Task<TeamLabControlScope> EnsurePlatformScopeAsync(CancellationToken cancellationToken)
    {
        var existing = await context.TeamLabControlScopes.SingleOrDefaultAsync(
            item => item.Key == PlatformScopeKey, cancellationToken);
        if (existing is not null) return existing;

        var scope = new TeamLabControlScope { Key = PlatformScopeKey, DisplayName = "Platform" };
        context.TeamLabControlScopes.Add(scope);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return scope;
        }
        catch (DbUpdateException)
        {
            context.Entry(scope).State = EntityState.Detached;
            return await context.TeamLabControlScopes.SingleAsync(
                item => item.Key == PlatformScopeKey, cancellationToken);
        }
    }

    public async Task<TeamLabControlScopeModel> CreateAsync(
        CreateTeamLabControlScopeModel command,
        bool administrator,
        CancellationToken cancellationToken)
    {
        if (!administrator)
            throw new TeamLabApiContractException("insufficient_permission", "仅管理员可以创建 TeamLab 控制范围", 403);

        var key = NormalizeKey(command.Key);
        var name = command.DisplayName.Trim();
        if (name.Length is < 1 or > 128)
            throw new TeamLabApiContractException("scope_display_name_invalid", "控制范围显示名称无效", 422);
        if (await context.TeamLabControlScopes.AnyAsync(item => item.Key == key, cancellationToken))
            throw new TeamLabApiContractException("scope_key_conflict", "控制范围 key 已存在", 409);

        var scope = new TeamLabControlScope { Key = key, DisplayName = name };
        context.TeamLabControlScopes.Add(scope);
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(scope);
    }

    public async Task<TeamLabControlScope> RequireWritableAsync(Guid scopeId, CancellationToken cancellationToken)
    {
        var scope = await context.TeamLabControlScopes.SingleOrDefaultAsync(item => item.Id == scopeId, cancellationToken)
            ?? throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab 控制范围", 404);
        if (scope.IsArchived)
            throw new TeamLabApiContractException("scope_archived", "该 TeamLab 控制范围已归档", 409);
        return scope;
    }

    /// <summary>
    /// Archives a control scope: existing resources stay readable and drainable,
    /// but no new writes (rollouts, runtimes, topologies, webhooks) may target it.
    /// Idempotent — archiving an archived scope succeeds.
    /// </summary>
    public async Task ArchiveAsync(Guid scopeId, CancellationToken cancellationToken)
    {
        var updated = await context.TeamLabControlScopes
            .Where(item => item.Id == scopeId && !item.IsArchived)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsArchived, true)
                .SetProperty(item => item.UpdatedAt, _ => DateTimeOffset.UtcNow),
                cancellationToken);
        if (updated == 0 && !await context.TeamLabControlScopes.AnyAsync(item => item.Id == scopeId, cancellationToken))
            throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab 控制范围", 404);
        if (imagePreparation is not null)
            await imagePreparation.ReleaseScopeAsync(scopeId, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamLabControlScopeModel>> ListGrantedAsync(
        IReadOnlyList<Guid> grantedScopeIds,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var query = context.TeamLabControlScopes.AsNoTracking();
        if (!administrator)
            query = query.Where(scope => grantedScopeIds.Contains(scope.Id));
        var rows = await query.OrderBy(scope => scope.Key).ToArrayAsync(cancellationToken);
        return rows.Select(ToModel).ToArray();
    }

    public static TeamLabControlScopeModel ToModel(TeamLabControlScope scope) => new(
        scope.Id, scope.Key, scope.DisplayName, scope.IsArchived, scope.CreatedAt, scope.UpdatedAt);

    private static string NormalizeKey(string value)
    {
        var key = value.Trim().ToLowerInvariant();
        if (key.Length is < 1 or > 96 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new TeamLabApiContractException("scope_key_invalid", "控制范围 key 无效", 422);
        return key;
    }
}
