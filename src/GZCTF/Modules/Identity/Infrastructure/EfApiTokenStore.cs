using GZCTF.Models;
using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Identity.Infrastructure;

public sealed class EfApiTokenStore(AppDbContext context) : IApiTokenStore
{
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
        var result = await context.ApiTokens
            .AsNoTracking()
            .Where(token => token.Id == id)
            .Select(token => new
            {
                Token = token,
                CreatorRole = context.Users
                    .Where(user => user.Id == token.CreatorId)
                    .Select(user => (Role?)user.Role)
                    .SingleOrDefault(),
                Scopes = token.Scopes.ToList(),
                Resources = token.Resources.ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (result?.CreatorRole is not { } creatorRole)
            return null;

        result.Token.Scopes = result.Scopes;
        result.Token.Resources = result.Resources;
        return new ApiTokenValidationRecord(result.Token, creatorRole);
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

        return affectedRows > 0;
    }

    public Task RecordUsageAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken) =>
        context.ApiTokens
            .Where(token => token.Id == id &&
                            (token.LastUsedAt == null || token.LastUsedAt < usedAt.AddMinutes(-1)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.LastUsedAt, _ => usedAt), cancellationToken);
}
