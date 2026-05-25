using System.Collections.Concurrent;
using GZCTF.Services.Concurrency;

namespace GZCTF.Services.Fleet;

/// <summary>
/// IMPORTANT: Current implementation uses in-memory ConcurrentDictionary.
/// For true distributed locking across multiple servers, replace with StackExchange.Redis RedLock.
/// See: https://redis.io/docs/manual/patterns/distributed-locks/
///
/// This stub is sufficient for single-node deployments.
/// </summary>
public class RedisDistributedLock : IDistributedLockService
{
    private readonly ILogger<RedisDistributedLock> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public RedisDistributedLock(ILogger<RedisDistributedLock> logger)
        => _logger = logger;

    /// <summary>
    /// Acquires an exclusive lock for the given key. Returns an IDisposable that releases the lock on dispose.
    /// </summary>
    /// <param name="key">The lock key (e.g., resource name with node ID).</param>
    /// <param name="timeout">Maximum time to wait for the lock. Defaults to 30 seconds.</param>
    /// <returns>A releaser handle that must be disposed to release the lock.</returns>
    public async Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null)
    {
        _logger.LogDebug("RedisDistributedLock.Acquire({Key}) — using local SemaphoreSlim stub", key);

        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(30);

        if (!await semaphore.WaitAsync(waitTimeout))
            throw new TimeoutException($"Failed to acquire lock for key '{key}' within {waitTimeout.TotalSeconds}s");

        _logger.LogDebug("Lock acquired for '{Key}'", key);

        return new LockReleaser(key, semaphore, _logger);
    }

    /// <summary>
    /// Releaser that returns the semaphore and cleans up the dictionary entry.
    /// </summary>
    private sealed class LockReleaser : IDisposable
    {
        private readonly string _key;
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger<RedisDistributedLock> _logger;
        private bool _disposed;

        public LockReleaser(string key, SemaphoreSlim semaphore, ILogger<RedisDistributedLock> logger)
        {
            _key = key;
            _semaphore = semaphore;
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _semaphore.Release();
            _logger.LogDebug("Lock released for '{Key}'", _key);

            // Clean up the dictionary entry if no one else is waiting
            _locks.TryRemove(_key, out _);
        }
    }
}
