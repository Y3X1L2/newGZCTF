using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Applies the external TeamLab scope boundary before any scoped resource is
/// resolved. It is deliberately limited to API-token grants, not a second
/// identity or organization model.
/// </summary>
public sealed class TeamLabScopeAuthorizationService(AppDbContext context)
{
    public async Task RequireReadableAsync(
        Guid scopeId,
        Guid? apiTokenId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var exists = await context.TeamLabControlScopes.AsNoTracking()
            .AnyAsync(scope => scope.Id == scopeId, cancellationToken);
        if (!exists || !await HasGrantAsync(scopeId, apiTokenId, administrator, cancellationToken))
            throw NotFound();
    }

    public async Task RequireWritableAsync(
        Guid scopeId,
        Guid? apiTokenId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var scope = await context.TeamLabControlScopes.AsNoTracking()
            .Select(item => new { item.Id, item.IsArchived })
            .SingleOrDefaultAsync(item => item.Id == scopeId, cancellationToken);
        if (scope is null || !await HasGrantAsync(scopeId, apiTokenId, administrator, cancellationToken))
            throw NotFound();
        if (scope.IsArchived)
            throw new TeamLabApiContractException(
                "scope_archived", "该 TeamLab 控制范围已归档，无法执行写入操作。", 409);
    }

    public async Task<Guid> RequireTopologyScopeAsync(
        Guid topologyId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var scopeId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.PublicId == topologyId)
            .Select(item => item.ControlScopeId)
            .SingleOrDefaultAsync(cancellationToken);
        return await RequireResourceScopeAsync(scopeId, apiTokenId, administrator, writable, cancellationToken);
    }

    public async Task<Guid> RequireReleaseScopeAsync(
        Guid releaseId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var scopeId = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == releaseId)
            .Select(item => item.ControlScopeId)
            .SingleOrDefaultAsync(cancellationToken);
        return await RequireResourceScopeAsync(scopeId, apiTokenId, administrator, writable, cancellationToken);
    }

    public async Task<Guid> RequireRuntimeScopeAsync(
        Guid runtimeId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var scopeId = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => item.ControlScopeId)
            .SingleOrDefaultAsync(cancellationToken);
        return await RequireResourceScopeAsync(scopeId, apiTokenId, administrator, writable, cancellationToken);
    }

    public async Task<Guid> RequireRolloutScopeAsync(
        Guid rolloutId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var scopeId = await context.TeamLabRollouts.AsNoTracking()
            .Where(item => item.PublicId == rolloutId)
            .Select(item => item.ControlScopeId)
            .SingleOrDefaultAsync(cancellationToken);
        return await RequireResourceScopeAsync(scopeId, apiTokenId, administrator, writable, cancellationToken);
    }

    public async Task RequireLinkPolicyScopeAsync(
        Guid policyId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var runtimeId = await context.TeamLabLinkPolicies.AsNoTracking()
            .Where(item => item.PublicId == policyId)
            .Select(item => item.Runtime.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
        if (runtimeId == Guid.Empty)
            throw new TeamLabApiContractException("link_policy_not_found", "未找到链路策略", 404);
        await RequireRuntimeScopeAsync(runtimeId, apiTokenId, administrator, writable, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ListReadableScopesAsync(
        Guid? apiTokenId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        if (administrator)
            return (await context.TeamLabControlScopes.AsNoTracking()
                .Select(scope => scope.Id).ToArrayAsync(cancellationToken)).ToHashSet();
        return (await context.ApiTokenResourceGrants.AsNoTracking()
                .Where(grant => grant.TokenId == apiTokenId && grant.ResourceType == "teamlab-scope")
                .Select(grant => grant.ResourceId)
                .ToArrayAsync(cancellationToken))
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    private async Task<Guid> RequireResourceScopeAsync(
        Guid? scopeId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        if (scopeId is not { } resolvedScope)
            throw NotFound();
        if (writable)
            await RequireWritableAsync(resolvedScope, apiTokenId, administrator, cancellationToken);
        else
            await RequireReadableAsync(resolvedScope, apiTokenId, administrator, cancellationToken);
        return resolvedScope;
    }

    /// <summary>
    /// A grant is satisfied by an exact scope grant or by a wildcard grant
    /// ("teamlab-scope:*" or the global "*:*"). The `administrator` flag is
    /// reserved for cookie-side actors (browser administrators); API tokens
    /// must never bypass the grant table via their creator's role.
    /// </summary>
    private Task<bool> HasGrantAsync(Guid scopeId, Guid? apiTokenId, bool administrator, CancellationToken cancellationToken) =>
        administrator
            ? Task.FromResult(true)
            : context.ApiTokenResourceGrants.AsNoTracking().AnyAsync(grant =>
                grant.TokenId == apiTokenId && grant.ResourceType == "teamlab-scope" &&
                (grant.ResourceId == scopeId.ToString("D") || grant.ResourceId == "*"), cancellationToken);

    private static TeamLabApiContractException NotFound() =>
        new("scope_not_found", "未找到 TeamLab 控制范围。", 404);
}
