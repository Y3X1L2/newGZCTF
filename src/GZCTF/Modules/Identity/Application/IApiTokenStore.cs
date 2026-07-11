using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Identity.Application;

public sealed record ApiTokenValidationRecord(ApiTokenEntity Token, Role CreatorRole);

public interface IApiTokenStore
{
    Task AddAsync(ApiTokenEntity token, CancellationToken cancellationToken);
    Task<ApiTokenEntity?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<ApiTokenValidationRecord?> FindForValidationAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiTokenEntity>> ListAsync(Guid? creatorId, CancellationToken cancellationToken);
    Task<bool> RevokeAsync(Guid id, Guid actorId, bool allowAny, CancellationToken cancellationToken);
    Task RecordUsageAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken);
}
