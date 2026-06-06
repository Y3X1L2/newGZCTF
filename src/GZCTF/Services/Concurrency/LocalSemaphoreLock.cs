namespace GZCTF.Services.Concurrency;

/// <summary>
/// Local semaphore-based lock for single-node deployments.
/// Uses an in-process SemaphoreSlim without external dependencies.
/// </summary>
public class LocalSemaphoreLock : IDistributedLockService
{
    private readonly ILogger<LocalSemaphoreLock> _logger;

    public LocalSemaphoreLock(ILogger<LocalSemaphoreLock> logger)
        => _logger = logger;

    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null)
    {
        _logger.LogDebug("LocalSemaphoreLock.Acquire({Key})", key);

        var semaphore = LocalLockPool.GetOrCreate(key);
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(30);

        if (!await semaphore.WaitAsync(waitTimeout))
            throw new TimeoutException($"Failed to acquire local lock for key '{key}' within {waitTimeout.TotalSeconds}s");

        _logger.LogDebug("Local lock acquired for '{Key}'", key);

        return new LocalLockReleaser(key, _logger);
    }

    private sealed class LocalLockReleaser : IDisposable
    {
        private readonly string _key;
        private readonly ILogger<LocalSemaphoreLock> _logger;
        private bool _disposed;

        public LocalLockReleaser(string key, ILogger<LocalSemaphoreLock> logger)
        {
            _key = key;
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            LocalLockPool.Release(_key);
            _logger.LogDebug("Local lock released for '{Key}'", _key);
        }
    }

    /// <summary>
    /// Static pool of named semaphores shared across all LocalSemaphoreLock instances.
    /// </summary>
    private static class LocalLockPool
    {
        private static readonly Dictionary<string, SemaphoreSlim> _locks = new();
        private static readonly object _syncRoot = new();

        public static SemaphoreSlim GetOrCreate(string key)
        {
            lock (_syncRoot)
            {
                if (!_locks.TryGetValue(key, out var semaphore))
                {
                    semaphore = new SemaphoreSlim(1, 1);
                    _locks[key] = semaphore;
                }
                return semaphore;
            }
        }

        public static void Release(string key)
        {
            lock (_syncRoot)
            {
                if (_locks.TryGetValue(key, out var semaphore))
                {
                    semaphore.Release();
                    // Clean up if no one is waiting
                    if (semaphore.CurrentCount >= 1)
                        _locks.Remove(key);
                }
            }
        }
    }
}
