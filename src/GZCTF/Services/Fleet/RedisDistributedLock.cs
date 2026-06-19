using System.Collections.Concurrent;
using System.Security.Cryptography;
using GZCTF.Services.Concurrency;
using StackExchange.Redis;

namespace GZCTF.Services.Fleet;

/// <summary>
/// Redis-backed lock used by fleet mode. Falls back to an in-process lock only
/// when Redis is not configured, so standalone development remains usable.
/// </summary>
public class RedisDistributedLock : IDistributedLockService, IDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new();
    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultLockExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimumLockExpiry = TimeSpan.FromSeconds(30);
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @token then return redis.call('del', @key) else return 0 end");

    private readonly ILogger<RedisDistributedLock> _logger;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IDatabase? _database;
    private readonly TimeSpan _lockExpiry;

    public RedisDistributedLock(IConfiguration config, ILogger<RedisDistributedLock> logger)
    {
        _logger = logger;
        _lockExpiry = TimeSpan.FromSeconds(Math.Max(
            MinimumLockExpiry.TotalSeconds,
            config.GetValue("Fleet:DistributedLockExpirySeconds", (int)DefaultLockExpiry.TotalSeconds)));
        var connectionString = config.GetConnectionString("RedisCache");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("Fleet distributed lock is using local fallback because RedisCache is not configured");
            return;
        }

        try
        {
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _database = _redis.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fleet distributed lock failed to connect to Redis");
            throw new InvalidOperationException("Fleet distributed lock requires a reachable RedisCache connection.", ex);
        }
    }

    public async Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null)
    {
        if (_database is null)
            return await AcquireLocalAsync(key, timeout);

        var waitTimeout = timeout ?? DefaultWaitTimeout;
        var deadline = DateTimeOffset.UtcNow + waitTimeout;
        var lockKey = MakeRedisKey(key);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        while (DateTimeOffset.UtcNow <= deadline)
        {
            if (await _database.LockTakeAsync(lockKey, token, _lockExpiry))
            {
                _logger.LogDebug("Redis lock acquired for '{Key}'", key);
                return new RedisLockReleaser(_database, lockKey, token, _lockExpiry, _logger);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Failed to acquire Redis lock for key '{key}' within {waitTimeout.TotalSeconds}s");
    }

    public void Dispose() => _redis?.Dispose();

    static RedisKey MakeRedisKey(string key) => $"gzctf:lock:{key}";

    async Task<IDisposable> AcquireLocalAsync(string key, TimeSpan? timeout)
    {
        var waitTimeout = timeout ?? DefaultWaitTimeout;
        var semaphore = LocalLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        if (!await semaphore.WaitAsync(waitTimeout))
            throw new TimeoutException($"Failed to acquire local fallback lock for key '{key}' within {waitTimeout.TotalSeconds}s");

        _logger.LogDebug("Local fallback lock acquired for '{Key}'", key);
        return new LocalLockReleaser(key, semaphore, _logger);
    }

    private sealed class RedisLockReleaser : IDisposable
    {
        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _token;
        private readonly TimeSpan _expiry;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _renewalCts = new();
        private readonly Task _renewalTask;
        private bool _disposed;

        public RedisLockReleaser(IDatabase database, RedisKey key, RedisValue token, TimeSpan expiry, ILogger logger)
        {
            _database = database;
            _key = key;
            _token = token;
            _expiry = expiry;
            _logger = logger;
            _renewalTask = Task.Run(RenewUntilDisposedAsync);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _renewalCts.Cancel();
            try
            {
                _database.ScriptEvaluate(ReleaseScript, new { key = _key, token = _token });
                _logger.LogDebug("Redis lock released for '{Key}'", _key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release Redis lock for '{Key}'", _key);
            }
            finally
            {
                _renewalTask.ContinueWith(_ => _renewalCts.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        async Task RenewUntilDisposedAsync()
        {
            var interval = TimeSpan.FromMilliseconds(Math.Max(1000, _expiry.TotalMilliseconds / 3));

            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(_renewalCts.Token))
                {
                    var renewed = await _database.LockExtendAsync(_key, _token, _expiry);
                    if (!renewed)
                    {
                        _logger.LogWarning("Redis lock renewal failed for '{Key}'. The lock may have expired or been stolen.",
                            _key);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_renewalCts.IsCancellationRequested)
            {
                // Expected on dispose.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis lock renewal loop stopped for '{Key}'", _key);
            }
        }
    }

    private sealed class LocalLockReleaser : IDisposable
    {
        private readonly string _key;
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger _logger;
        private bool _disposed;

        public LocalLockReleaser(string key, SemaphoreSlim semaphore, ILogger logger)
        {
            _key = key;
            _semaphore = semaphore;
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _semaphore.Release();
            _logger.LogDebug("Local fallback lock released for '{Key}'", _key);

            if (_semaphore.CurrentCount > 0)
                LocalLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(_key, _semaphore));
        }
    }
}
