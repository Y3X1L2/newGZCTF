namespace GZCTF.Modules.Identity.Application;

public sealed record ApiTokenRateLimitDecision(bool Available, bool Allowed, int RetryAfterSeconds);

public interface IApiTokenRateLimitStore
{
    Task<ApiTokenRateLimitDecision> ConsumeAsync(Guid tokenId, int requestsPerMinute);
}
