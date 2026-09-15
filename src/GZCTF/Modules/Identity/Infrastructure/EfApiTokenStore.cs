using GZCTF.Models;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Identity.Infrastructure;

public sealed class EfApiTokenStore(AppDbContext context, IMemoryCache cache) : IApiTokenStore
{
    private static readonly TimeSpan MetadataCacheLifetime = TimeSpan.FromMinutes(10);

    public async Task AddAsync(ApiTokenEntity token, CancellationToken cancellationToken)
    {
        context.ApiTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<ApiTokenEntity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.ApiTokens
            .Include(token => token.Scopes)
            .Include(token => token.Resources)
            .SingleOrDefaultAsync(token => token.Id == id, cancellationToken);

    public async Task<ApiTokenValidationRecord?> FindForValidationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var state = await context.ApiTokens
            .AsNoTracking()
            .Where(token => token.Id == id)
            .Select(token => new
            {
                token.CreatorId,
                token.ExpiresAt,
                token.LastUsedAt,
                token.RevokedAt,
                CreatorRole = context.Users
                    .Where(user => user.Id == token.CreatorId)
                    .Select(user => (Role?)user.Role)
                    .SingleOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (state?.CreatorRole is not { } creatorRole)
            return null;

        var metadata = await cache.GetOrCreateAsync(
            CacheKey(id),
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = MetadataCacheLifetime;
                return await context.ApiTokens.AsNoTracking()
                    .Where(token => token.Id == id)
                    .Select(token => new CachedTokenMetadata(
                        token.Name,
                        token.SecretHash,
                        token.RequestsPerMinute,
                        token.CreatedAt,
                        token.Scopes.Select(scope => scope.Scope).ToArray(),
                        token.Resources.Select(resource => new CachedResourceGrant(
                            resource.ResourceType, resource.ResourceId)).ToArray()))
                    .SingleOrDefaultAsync(cancellationToken);
            });
        if (metadata is null)
            return null;

        var token = new ApiTokenEntity
        {
            Id = id,
            Name = metadata.Name,
            CreatorId = state.CreatorId,
            SecretHash = metadata.SecretHash,
            RequestsPerMinute = metadata.RequestsPerMinute,
            CreatedAt = metadata.CreatedAt,
            ExpiresAt = state.ExpiresAt,
            LastUsedAt = state.LastUsedAt,
            RevokedAt = state.RevokedAt,
            Scopes = metadata.Scopes.Select(scope => new ApiTokenScopeGrant
            {
                TokenId = id,
                Scope = scope
            }).ToList(),
            Resources = metadata.Resources.Select(resource => new ApiTokenResourceGrant
            {
                TokenId = id,
                ResourceType = resource.ResourceType,
                ResourceId = resource.ResourceId
            }).ToList()
        };
        return new ApiTokenValidationRecord(token, creatorRole);
    }

    public async Task<IReadOnlyList<ApiTokenEntity>> ListAsync(
        Guid? creatorId,
        CancellationToken cancellationToken)
    {
        var query = context.ApiTokens
            .AsNoTracking()
            .Include(token => token.Scopes)
            .Include(token => token.Resources)
            .AsQueryable();
        if (creatorId.HasValue)
            query = query.Where(token => token.CreatorId == creatorId.Value);

        return await query.OrderByDescending(token => token.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<bool> RevokeAsync(
        Guid id,
        Guid actorId,
        bool allowAny,
        CancellationToken cancellationToken)
    {
        var affectedRows = await context.ApiTokens
            .Where(token => token.Id == id && (allowAny || token.CreatorId == actorId))
            .Where(token => token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, _ => DateTimeOffset.UtcNow), cancellationToken);

        if (affectedRows > 0)
            cache.Remove(CacheKey(id));
        return affectedRows > 0;
    }

    public Task RecordUsageAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken) =>
        context.ApiTokens
            .Where(token => token.Id == id &&
                            (token.LastUsedAt == null || token.LastUsedAt < usedAt.AddMinutes(-1)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.LastUsedAt, _ => usedAt), cancellationToken);

    private static string CacheKey(Guid id) => $"api-token:metadata:{id:N}";

    private sealed record CachedTokenMetadata(
        string Name,
        byte[] SecretHash,
        int RequestsPerMinute,
        DateTimeOffset CreatedAt,
        string[] Scopes,
        CachedResourceGrant[] Resources);

    private sealed record CachedResourceGrant(string ResourceType, string ResourceId);
}
