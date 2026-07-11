using StackExchange.Redis;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Identity.Infrastructure;

public sealed class ApiTokenRateLimitStore : IApiTokenRateLimitStore, IDisposable
{
    private const string ConsumeScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('TTL', KEYS[1])
        return {current, ttl}
        """;

    private readonly IConnectionMultiplexer? _connection;
    private readonly IDatabase? _database;
    private readonly ILogger<ApiTokenRateLimitStore> _logger;

    public ApiTokenRateLimitStore(IConfiguration configuration, ILogger<ApiTokenRateLimitStore> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("RedisCache");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 1;
            options.ConnectTimeout = 2_000;
            _connection = ConnectionMultiplexer.Connect(options);
            _database = _connection.GetDatabase();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize API token rate limiter Redis connection");
        }
    }

    public async Task<ApiTokenRateLimitDecision> ConsumeAsync(Guid tokenId, int requestsPerMinute)
    {
        if (_database is null)
            return new ApiTokenRateLimitDecision(false, false, 0);

        try
        {
            var result = (RedisResult[]?)await _database.ScriptEvaluateAsync(
                ConsumeScript,
                [$"gzctf:api-token-rate:{tokenId:N}"],
                [60]);
            if (result is not [var countResult, var ttlResult])
                return new ApiTokenRateLimitDecision(false, false, 0);

            var count = (long)countResult;
            var ttl = Math.Max(1, (int)(long)ttlResult);
            return new ApiTokenRateLimitDecision(true, count <= requestsPerMinute, ttl);
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "API token rate limiter Redis operation failed");
            return new ApiTokenRateLimitDecision(false, false, 0);
        }
    }

    public void Dispose() => _connection?.Dispose();
}
