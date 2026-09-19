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
        await RequireScopeAsync(
            context.TeamLabControlScopes.Where(scope => scope.Id == scopeId).Select(scope => (Guid?)scope.Id),
            apiTokenId, administrator, false, cancellationToken);
    }

    public async Task RequireWritableAsync(
        Guid scopeId,
        Guid? apiTokenId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        await RequireScopeAsync(
            context.TeamLabControlScopes.Where(scope => scope.Id == scopeId).Select(scope => (Guid?)scope.Id),
            apiTokenId, administrator, true, cancellationToken);
    }

    public async Task<Guid> RequireTopologyScopeAsync(
        Guid topologyId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        return await RequireScopeAsync(
            context.TeamLabTopologies.Where(item => item.PublicId == topologyId).Select(item => item.ControlScopeId),
            apiTokenId, administrator, writable, cancellationToken);
    }

    public async Task<Guid> RequireReleaseScopeAsync(
        Guid releaseId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        return await RequireScopeAsync(
            context.TeamLabTopologyReleases.Where(item => item.Id == releaseId).Select(item => item.ControlScopeId),
            apiTokenId, administrator, writable, cancellationToken);
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
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        var allowed = administrator ||
            await context.ApiTokenResourceGrants.AsNoTracking().AnyAsync(grant =>
                grant.TokenId == apiTokenId && grant.ResourceType == "teamlab-scope" &&
                (grant.ResourceId == scopeId.ToString() || grant.ResourceId == "*"), cancellationToken);
        if (!allowed) throw NotFound();
        if (writable && await context.TeamLabControlScopes.AsNoTracking()
                .AnyAsync(item => item.Id == scopeId && item.IsArchived, cancellationToken))
            throw new TeamLabApiContractException(
                "scope_archived", "该 TeamLab 控制范围已归档，无法执行写入操作。", 409);
        return scopeId;
    }

    public async Task<Guid?> RequireConnectorScopeAsync(
        Guid connectorId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var connector = await context.TeamLabConnectors.AsNoTracking()
            .Where(item => item.PublicId == connectorId)
            .Select(item => new ConnectorScope(item.ControlScopeId))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("connector_not_found", "未找到连接器", 404);
        if (connector.ControlScopeId is not { } scopeId)
        {
            if (!administrator)
                throw new TeamLabApiContractException("connector_not_found", "未找到连接器", 404);
            return null;
        }

        if (writable)
            await RequireWritableAsync(scopeId, apiTokenId, administrator, cancellationToken);
        else
            await RequireReadableAsync(scopeId, apiTokenId, administrator, cancellationToken);
        return scopeId;
    }

    public async Task<Guid> RequireRolloutScopeAsync(
        Guid rolloutId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        return await RequireScopeAsync(
            context.TeamLabRollouts.Where(item => item.PublicId == rolloutId).Select(item => item.ControlScopeId),
            apiTokenId, administrator, writable, cancellationToken);
    }

    public async Task RequireLinkPolicyScopeAsync(
        Guid policyId,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var found = await RequireScopeAsync(
            context.TeamLabLinkPolicies.Where(item => item.PublicId == policyId)
                .Select(item => item.Runtime.ControlScopeId),
            apiTokenId, administrator, writable, cancellationToken,
            () => new TeamLabApiContractException("link_policy_not_found", "未找到链路策略", 404));
        _ = found;
    }

    public async Task<IReadOnlySet<Guid>> ListReadableScopesAsync(
        Guid? apiTokenId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        if (administrator)
            return (await context.TeamLabControlScopes.AsNoTracking()
                .Select(scope => scope.Id).ToArrayAsync(cancellationToken)).ToHashSet();
        var grants = await context.ApiTokenResourceGrants.AsNoTracking()
                .Where(grant => grant.TokenId == apiTokenId && grant.ResourceType == "teamlab-scope")
                .Select(grant => grant.ResourceId)
                .ToArrayAsync(cancellationToken);
        if (grants.Contains("*", StringComparer.Ordinal))
            return (await context.TeamLabControlScopes.AsNoTracking()
                .Select(scope => scope.Id).ToArrayAsync(cancellationToken)).ToHashSet();
        return grants
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    private async Task<Guid> RequireScopeAsync(
        IQueryable<Guid?> resourceScopeIds,
        Guid? apiTokenId,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken,
        Func<TeamLabApiContractException>? notFound = null)
    {
        var scope = await context.TeamLabControlScopes.AsNoTracking()
            .Where(item => resourceScopeIds.Contains(item.Id))
            .Where(item => administrator || context.ApiTokenResourceGrants.Any(grant =>
                grant.TokenId == apiTokenId && grant.ResourceType == "teamlab-scope" &&
                (grant.ResourceId == item.Id.ToString() || grant.ResourceId == "*")))
            .Select(item => new { item.Id, item.IsArchived })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            throw notFound?.Invoke() ?? NotFound();
        if (writable && scope.IsArchived)
            throw new TeamLabApiContractException(
                "scope_archived", "该 TeamLab 控制范围已归档，无法执行写入操作。", 409);
        return scope.Id;
    }

    private static TeamLabApiContractException NotFound() =>
        new("scope_not_found", "未找到 TeamLab 控制范围。", 404);

    private sealed record ConnectorScope(Guid? ControlScopeId);
}
