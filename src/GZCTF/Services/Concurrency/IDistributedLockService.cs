namespace GZCTF.Services.Concurrency;

/// <summary>
/// Distributed lock service for cross-node synchronization in Fleet mode,
/// or local semaphore-based locking in standalone mode.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Acquire an exclusive lock for the given key.
    /// Returns an IDisposable handle that releases the lock on dispose.
    /// </summary>
    /// <param name="key">The lock key (e.g., resource name with node ID).</param>
    /// <param name="timeout">Maximum time to wait for the lock.</param>
    /// <returns>A releaser handle that must be disposed to release the lock.</returns>
    Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null);
}
