using GZCTF.Modules.Identity.Application;

namespace GZCTF.Integration.Test.Tests.Api.Fixtures;

public sealed class FaultInjectingApiTokenRateLimitStore(IApiTokenRateLimitStore inner)
    : IApiTokenRateLimitStore
{
    private bool _available = true;

    public bool Available
    {
        get => Volatile.Read(ref _available);
        set => Volatile.Write(ref _available, value);
    }

    public Task<ApiTokenRateLimitDecision> ConsumeAsync(Guid tokenId, int requestsPerMinute) =>
        Available
            ? inner.ConsumeAsync(tokenId, requestsPerMinute)
            : Task.FromResult(new ApiTokenRateLimitDecision(false, false, 0));
}
