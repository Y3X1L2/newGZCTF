namespace GZCTF.Modules.Identity.Application;

public sealed record ApiTokenRateLimitDecision(
    bool Available,
    bool Allowed,
    int Limit,
    int Remaining,
    int ResetAfterSeconds);

public interface IApiTokenRateLimitStore
{
    Task<ApiTokenRateLimitDecision> ConsumeAsync(Guid tokenId, int requestsPerMinute);
}
