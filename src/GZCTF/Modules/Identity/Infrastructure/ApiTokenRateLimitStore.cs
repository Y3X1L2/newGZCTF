using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.Identity.Application;
using StackExchange.Redis;

namespace GZCTF.Modules.Identity.Infrastructure;

public sealed class ApiTokenRateLimitStore(
    IRedisConnectionProvider connections,
    RedisKeyspace keyspace,
    ILogger<ApiTokenRateLimitStore> logger) : IApiTokenRateLimitStore
{
    private const string ConsumeScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('TTL', KEYS[1])
        return {current, ttl}
        """;

    public async Task<ApiTokenRateLimitDecision> ConsumeAsync(Guid tokenId, int requestsPerMinute)
    {
        try
        {
            var connection = await connections.GetAsync();
            if (connection is null)
                return new(false, false, 0);

            var result = (RedisResult[]?)await connection.GetDatabase().ScriptEvaluateAsync(
                ConsumeScript,
                [keyspace.CreateOpaque(RedisKeyPurpose.Lease, "api-rate", tokenId.ToString("N"))],
                [60]);
            if (result is not [var countResult, var ttlResult])
                return new(false, false, 0);

            var count = (long)countResult;
            var ttl = Math.Max(1, (int)(long)ttlResult);
            return new(true, count <= requestsPerMinute, ttl);
        }
        catch (Exception exception) when (exception is RedisException or InvalidOperationException)
        {
            logger.LogWarning(exception, "API token rate limiter Redis operation failed");
            return new(false, false, 0);
        }
    }
}
